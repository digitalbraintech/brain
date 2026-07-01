using DigitalBrain.Context;

namespace DigitalBrain.Tests.Context;

public class HybridScorerTests
{
    [Fact]
    public void Cosine_Ranks_Aligned_Vectors_Higher()
    {
        float[] query = [1f, 0f, 0f];
        Assert.True(HybridScorer.CosineSimilarity(query, [1f, 0f, 0f]) > HybridScorer.CosineSimilarity(query, [0f, 1f, 0f]));
        Assert.Equal(0f, HybridScorer.CosineSimilarity(query, [0f, 1f, 0f]), 3);
    }

    [Fact]
    public void Score_Falls_Back_To_Keyword_When_Embeddings_Are_Zero()
    {
        float[] zero = new float[3];
        var match = HybridScorer.Score("buy milk", "remember to buy milk today", zero, zero);
        var noMatch = HybridScorer.Score("buy milk", "git commit workflow", zero, zero);

        Assert.True(match > noMatch);
        Assert.True(match > 0f);
    }

    [Fact]
    public void Score_Blends_Vector_And_Keyword_When_Real()
    {
        float[] query = [1f, 0f];
        float[] aligned = [1f, 0f];
        // keyword hit (1.0) + perfect cosine (1.0) -> 0.6*1 + 0.4*1 = 1.0
        var blended = HybridScorer.Score("alpha", "alpha text", query, aligned);
        Assert.True(blended > 0.9f);
    }
}
