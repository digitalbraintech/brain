using System.Collections.Concurrent;

namespace DigitalBrain.Silo;

public sealed record VectorRecord(string Id, float[] Vector, string Payload);
public sealed record VectorHit(string Id, float Score, string Payload);

// Vector store abstraction for document RAG. Implemented in-memory (dev/test) and over Qdrant (production).
public interface IVectorStore
{
    Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default);
    Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default);
    Task<VectorHit[]> SearchAsync(string collection, float[] query, int top = 5, CancellationToken ct = default);
}

// In-memory cosine vector store (reuses HybridScorer.CosineSimilarity). The dependency-free default; lets the
// document-RAG pipeline be tested without a Qdrant container.
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, VectorRecord>> _collections = new();

    public Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default)
    {
        _collections.TryAdd(collection, new ConcurrentDictionary<string, VectorRecord>());
        return Task.CompletedTask;
    }

    public Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default)
    {
        var col = _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, VectorRecord>());
        foreach (var record in records)
            col[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task<VectorHit[]> SearchAsync(string collection, float[] query, int top = 5, CancellationToken ct = default)
    {
        if (!_collections.TryGetValue(collection, out var col))
            return Task.FromResult(Array.Empty<VectorHit>());

        var hits = col.Values
            .Select(r => new VectorHit(r.Id, HybridScorer.CosineSimilarity(query, r.Vector), r.Payload))
            .OrderByDescending(h => h.Score)
            .Take(top)
            .ToArray();
        return Task.FromResult(hits);
    }
}
