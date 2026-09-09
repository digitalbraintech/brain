using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;

namespace DigitalBrain.Scripting.Applications;

public sealed partial class ApplicationAuthoringService
{
    public async Task<ApplicationExpectations> SetExpectationsAsync(
        IDigitalBrain brain,
        string key,
        string documentJson,
        Guid operationId,
        string? expectedExpectationRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentJson);
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A stable expectation operation ID is required.", nameof(operationId));
        }
        using var parsedDocument = JsonDocument.Parse(documentJson);
        var location = LocationFor(brain, key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await ReadMetadataAsync(location, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Save the application source before recording expectations.");
        var revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(documentJson)));
        if (metadata.ExpectationOperations?.TryGetValue(operationId, out var replayRevision) == true)
        {
            if (!StringComparer.Ordinal.Equals(replayRevision, revision) ||
                metadata.Expectations?.GetValueOrDefault(replayRevision) is not { } replay)
            {
                throw new InvalidOperationException("The expectation operation ID was already used for different content.");
            }
            return await ToPublicAsync(key, replay, cancellationToken).ConfigureAwait(false);
        }
        if (!StringComparer.Ordinal.Equals(metadata.ExpectationRevision, expectedExpectationRevision))
        {
            throw new InvalidOperationException("The accepted expectations changed. Read their current revision before replacing them.");
        }

        var actor = VerifiedActor.Current!;
        var directory = Path.Combine(location.Directory, "expectations");
        Directory.CreateDirectory(directory);
        var documentPath = Path.Combine(directory, $"{revision}.json");
        if (!File.Exists(documentPath))
        {
            await WriteAtomicAsync(documentPath, Encoding.UTF8.GetBytes(documentJson), cancellationToken)
                .ConfigureAwait(false);
        }
        else if (!StringComparer.Ordinal.Equals(
                     await ReadExpectationDocumentAsync(documentPath, cancellationToken).ConfigureAwait(false), documentJson))
        {
            throw new InvalidDataException("An immutable expectation revision has conflicting content.");
        }

        var stored = metadata.Expectations?.GetValueOrDefault(revision)
            ?? new StoredExpectation(revision, documentPath, operationId, actor.PrincipalId.Value, DateTimeOffset.UtcNow);
        var expectations = new Dictionary<string, StoredExpectation>(
            metadata.Expectations ?? new Dictionary<string, StoredExpectation>(), StringComparer.Ordinal)
        {
            [revision] = stored,
        };
        var operations = new Dictionary<Guid, string>(metadata.ExpectationOperations ?? new Dictionary<Guid, string>())
        {
            [operationId] = revision,
        };
        await WriteMetadataAsync(location, metadata with
        {
            ExpectationRevision = revision,
            Expectations = expectations,
            ExpectationOperations = operations,
        }, cancellationToken).ConfigureAwait(false);
        return await ToPublicAsync(key, stored, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApplicationExpectations> ReadExpectationsAsync(
        IDigitalBrain brain,
        string key,
        string expectationRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectationRevision);
        var metadata = await ReadMetadataAsync(LocationFor(brain, key), cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The application has no recorded expectations.");
        var stored = metadata.Expectations?.GetValueOrDefault(expectationRevision)
            ?? throw new KeyNotFoundException("The expectation revision is not retained.");
        return await ToPublicAsync(key, stored, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ApplicationExpectations> ToPublicAsync(
        string key, StoredExpectation stored, CancellationToken cancellationToken)
        => new(key, stored.ExpectationRevision,
            await ReadExpectationDocumentAsync(stored.DocumentPath, cancellationToken).ConfigureAwait(false),
            stored.OperationId, stored.PrincipalId, stored.RecordedAt);

    private static async Task<string> ReadExpectationDocumentAsync(
        string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
