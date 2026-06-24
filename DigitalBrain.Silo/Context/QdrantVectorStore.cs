using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DigitalBrain.Silo;

// Qdrant-backed vector store (Qdrant.Client 1.17). The production backend for document RAG. Requires a running
// Qdrant (Aspire AddQdrant / a configured endpoint) — not exercised in unit tests, which use InMemoryVectorStore.
public sealed class QdrantVectorStore(QdrantClient client) : IVectorStore
{
    public async Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default)
    {
        if (!await client.CollectionExistsAsync(collection, ct))
        {
            await client.CreateCollectionAsync(
                collection,
                new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine },
                cancellationToken: ct);
        }
    }

    public async Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default)
    {
        var points = records.Select(r => new PointStruct
        {
            Id = DeterministicGuid(r.Id),
            Vectors = r.Vector,
            Payload =
            {
                ["docId"] = r.Id,
                ["text"] = r.Payload
            }
        }).ToList();

        if (points.Count > 0)
            await client.UpsertAsync(collection, points, cancellationToken: ct);
    }

    public async Task<VectorHit[]> SearchAsync(string collection, float[] query, int top = 5, CancellationToken ct = default)
    {
        var results = await client.SearchAsync(collection, query, limit: (ulong)top, cancellationToken: ct);
        return results.Select(p => new VectorHit(
            p.Payload.TryGetValue("docId", out var docId) ? docId.StringValue : p.Id.Uuid,
            p.Score,
            p.Payload.TryGetValue("text", out var text) ? text.StringValue : string.Empty)).ToArray();
    }

    // Stable point id from the record's string id (so re-ingesting the same chunk updates, not duplicates).
    private static Guid DeterministicGuid(string id) => new(MD5.HashData(Encoding.UTF8.GetBytes(id)));
}
