using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DigitalBrain.Memory.Qdrant;

internal sealed class QdrantVectorMemoryProvider : IAsyncDisposable
{
    internal const string DefaultCollectionName = "digitalbrain_vector_memory";

    private const string OwnerField = "owner";
    private const string NamespaceField = "namespace";
    private const string KeyField = "key";
    private const string TextField = "text";
    private const string MetadataPrefix = "m_";
    private const string PayloadIdField = "payload_id";
    private const string PayloadExpiresField = "payload_expires";

    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly SemaphoreSlim _collectionGate = new(1, 1);
    private int? _vectorSize;
    private bool _collectionReady;

    public QdrantVectorMemoryProvider(QdrantClient client, string? collectionName = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _collectionName = string.IsNullOrWhiteSpace(collectionName)
            ? DefaultCollectionName
            : collectionName;
    }

    public ValueTask DisposeAsync()
    {
        _collectionGate.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task UpsertAsync(
        string owner,
        string @namespace,
        string key,
        string text,
        IReadOnlyDictionary<string, string> metadata,
        ProtectedPayloadReference? payload,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding must contain at least one dimension.", nameof(embedding));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCollectionAsync(embedding.Length, cancellationToken).ConfigureAwait(false);
        ValidateDimension(embedding.Length);

        var point = new PointStruct
        {
            Id = ToPointId(owner, @namespace, key),
            Vectors = embedding,
            Payload =
            {
                [OwnerField] = owner,
                [NamespaceField] = @namespace,
                [KeyField] = key,
                [TextField] = text,
            },
        };

        foreach (var (metaKey, metaValue) in metadata)
        {
            point.Payload[MetadataPrefix + metaKey] = metaValue;
        }

        if (payload is { } protectedPayload)
        {
            point.Payload[PayloadIdField] = protectedPayload.Id.ToString("D");
            if (protectedPayload.ExpiresAt is { } expiresAt)
            {
                point.Payload[PayloadExpiresField] = expiresAt.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        try
        {
            await _client.UpsertAsync(
                    _collectionName,
                    [point],
                    wait: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Vector memory upsert failed.", ex);
        }
    }

    public async Task<IReadOnlyList<QdrantVectorMemoryHit>> SearchAsync(
        string owner,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();

        if (queryEmbedding.Length == 0)
        {
            throw new ArgumentException("Query embedding must contain at least one dimension.", nameof(queryEmbedding));
        }

        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        await EnsureCollectionAsync(queryEmbedding.Length, cancellationToken).ConfigureAwait(false);
        ValidateDimension(queryEmbedding.Length);

        var must = new List<Condition>
        {
            MatchKeyword(OwnerField, owner),
            MatchKeyword(NamespaceField, @namespace),
        };

        if (metadataFilter is not null)
        {
            foreach (var (metaKey, metaValue) in metadataFilter)
            {
                must.Add(MatchKeyword(MetadataPrefix + metaKey, metaValue));
            }
        }

        try
        {
            var results = await _client.QueryAsync(
                    _collectionName,
                    query: queryEmbedding,
                    filter: new Filter { Must = { must } },
                    limit: (ulong)limit,
                    payloadSelector: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return results
                .Select(static point => ToHit(point))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Vector memory search failed.", ex);
        }
    }

    public async Task<bool> RemoveAsync(
        string owner,
        string @namespace,
        string key,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var pointId = ToPointId(owner, @namespace, key);

        try
        {
            var existing = await _client.RetrieveAsync(
                    _collectionName,
                    [pointId],
                    withPayload: true,
                    withVectors: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (existing.Count == 0)
            {
                return false;
            }

            var payload = existing[0].Payload;
            if (!PayloadEquals(payload, OwnerField, owner)
                || !PayloadEquals(payload, NamespaceField, @namespace)
                || !PayloadEquals(payload, KeyField, key))
            {
                return false;
            }

            await _client.DeleteAsync(
                    _collectionName,
                    ids: [pointId],
                    wait: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Vector memory remove failed.", ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string owner,
        string @namespace,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var keys = new List<string>();
        PointId? offset = null;
        var filter = new Filter
        {
            Must =
            {
                MatchKeyword(OwnerField, owner),
                MatchKeyword(NamespaceField, @namespace),
            },
        };

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await _client.ScrollAsync(
                        _collectionName,
                        filter: filter,
                        limit: 256,
                        offset: offset,
                        payloadSelector: true,
                        vectorsSelector: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                foreach (var point in page.Result)
                {
                    var key = ReadString(point.Payload, KeyField);
                    if (key.Length > 0)
                    {
                        keys.Add(key);
                    }
                }

                if (page.NextPageOffset is null || page.Result.Count == 0)
                {
                    break;
                }

                offset = page.NextPageOffset;
            }

            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Vector memory list keys failed.", ex);
        }
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken)
    {
        if (_collectionReady && _vectorSize == vectorSize)
        {
            return;
        }

        await _collectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_collectionReady)
            {
                ValidateDimension(vectorSize);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var exists = await _client.CollectionExistsAsync(_collectionName, cancellationToken)
                .ConfigureAwait(false);

            if (exists)
            {
                var info = await _client.GetCollectionInfoAsync(_collectionName, cancellationToken)
                    .ConfigureAwait(false);
                var existingSize = ReadVectorSize(info);
                if (existingSize != (ulong)vectorSize)
                {
                    throw new InvalidOperationException(
                        $"Vector memory collection dimension is {existingSize}, but the embedding has {vectorSize} dimensions.");
                }

                _vectorSize = vectorSize;
                _collectionReady = true;
                return;
            }

            await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams
                    {
                        Size = (ulong)vectorSize,
                        Distance = Distance.Cosine,
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _vectorSize = vectorSize;
            _collectionReady = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Vector memory collection ensure failed.", ex);
        }
        finally
        {
            _collectionGate.Release();
        }
    }

    private async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _client.CollectionExistsAsync(_collectionName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Vector memory collection lookup failed.", ex);
        }
    }

    private void ValidateDimension(int vectorSize)
    {
        if (_vectorSize is int expected && expected != vectorSize)
        {
            throw new InvalidOperationException(
                $"Vector memory expects {expected}-dimension embeddings, received {vectorSize}.");
        }
    }

    private static ulong ReadVectorSize(CollectionInfo info)
    {
        if (info.Config?.Params?.VectorsConfig?.Params is { } vectorParams)
        {
            return vectorParams.Size;
        }

        throw new InvalidOperationException("Vector memory collection does not expose a fixed vector size.");
    }

    private static Condition MatchKeyword(string field, string value)
        => new()
        {
            Field = new FieldCondition
            {
                Key = field,
                Match = new Match { Keyword = value },
            },
        };

    private static QdrantVectorMemoryHit ToHit(ScoredPoint point)
    {
        var payload = point.Payload;
        var key = ReadString(payload, KeyField);
        var text = ReadString(payload, TextField);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (field, value) in payload)
        {
            if (field.StartsWith(MetadataPrefix, StringComparison.Ordinal))
            {
                metadata[field[MetadataPrefix.Length..]] = value.StringValue;
            }
        }

        ProtectedPayloadReference? protectedPayload = null;
        if (payload.TryGetValue(PayloadIdField, out var payloadIdValue)
            && Guid.TryParse(payloadIdValue.StringValue, out var payloadId))
        {
            DateTimeOffset? expiresAt = null;
            if (payload.TryGetValue(PayloadExpiresField, out var expiresValue)
                && DateTimeOffset.TryParse(
                    expiresValue.StringValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedExpires))
            {
                expiresAt = parsedExpires;
            }

            protectedPayload = new ProtectedPayloadReference(payloadId, expiresAt);
        }

        return new QdrantVectorMemoryHit(key, text, metadata, protectedPayload);
    }

    private static string ReadString(Google.Protobuf.Collections.MapField<string, Value> payload, string field)
        => payload.TryGetValue(field, out var value) ? value.StringValue : string.Empty;

    private static bool PayloadEquals(Google.Protobuf.Collections.MapField<string, Value> payload, string field, string expected)
        => payload.TryGetValue(field, out var value)
            && string.Equals(value.StringValue, expected, StringComparison.Ordinal);

    private static PointId ToPointId(string owner, string @namespace, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(owner + "\0" + @namespace + "\0" + key));
        return new PointId { Uuid = new Guid(bytes.AsSpan(0, 16)).ToString("D") };
    }
}

internal sealed record QdrantVectorMemoryHit(
    string Key,
    string Text,
    IReadOnlyDictionary<string, string> Metadata,
    ProtectedPayloadReference? Payload);
