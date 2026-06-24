namespace DigitalBrain.Silo;

// Pure-C# hybrid relevance scoring: cosine similarity over embeddings blended with keyword overlap.
// Harvested from IAW AgentRegistryGrain.HybridSearchAsync. Zero-vector detection lets recall fall back to
// keyword-only when embeddings are NoOp (no real embedding model configured) — so Context works with zero deps.
public static class HybridScorer
{
    // Combined score for one candidate against a query. With a real (non-zero) query embedding AND a real
    // candidate embedding, blends 0.6*cosine + 0.4*keyword; otherwise keyword-only.
    public static float Score(string query, string candidateText, ReadOnlySpan<float> queryEmbedding, ReadOnlySpan<float> candidateEmbedding)
    {
        var keyword = KeywordScore(query, candidateText);
        var hasRealQuery = queryEmbedding.Length > 0 && !IsZeroVector(queryEmbedding);
        var vector = hasRealQuery && candidateEmbedding.Length > 0 && !IsZeroVector(candidateEmbedding)
            ? CosineSimilarity(queryEmbedding, candidateEmbedding)
            : 0f;
        return hasRealQuery && vector > 0f ? 0.6f * vector + 0.4f * keyword : keyword;
    }

    public static float KeywordScore(string query, string text)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0) return 0f;
        var haystack = text.ToLowerInvariant();
        var hits = terms.Count(t => haystack.Contains(t, StringComparison.Ordinal));
        return (float)hits / terms.Count;
    }

    public static bool IsZeroVector(ReadOnlySpan<float> v)
    {
        for (var i = 0; i < v.Length; i++)
            if (v[i] != 0f) return false;
        return true;
    }

    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0f || normB == 0f ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private static HashSet<string> Tokenize(string s) =>
        s.Split([' ', ',', '.', '-', '_', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
         .Select(t => t.ToLowerInvariant())
         .ToHashSet();
}
