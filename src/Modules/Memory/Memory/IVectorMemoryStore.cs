using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

internal interface IVectorMemoryStore
{
    Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorMemoryMatch>> SearchAsync(
        string owner,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string owner, string @namespace, string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListKeysAsync(string owner, string @namespace, CancellationToken cancellationToken);
}

internal sealed record VectorMemoryEntry(
    string Owner,
    string Namespace,
    string Key,
    string Text,
    IReadOnlyDictionary<string, string> Metadata,
    ProtectedPayloadReference? Payload,
    float[] Embedding);
