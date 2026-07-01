using DigitalBrain.Context;
using DigitalBrain.Kernel.Llm;

namespace DigitalBrain.Tests.Context;

public class DocumentIngestionTests
{
    [Fact]
    public void TextChunker_Splits_Long_Text_Into_Multiple_Chunks()
    {
        var text = string.Join("\n\n",
            Enumerable.Range(0, 10).Select(i => string.Join(' ', Enumerable.Repeat($"w{i}", 100))));

        var chunks = TextChunker.Chunk(text, targetWords: 150, maxWords: 300);
        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public async Task InMemory_Vector_Store_Cosine_Search_Ranks_Aligned_First()
    {
        var store = new InMemoryVectorStore();
        await store.EnsureCollectionAsync("c", 3);
        await store.UpsertAsync("c",
        [
            new VectorRecord("a", [1f, 0f, 0f], "A"),
            new VectorRecord("b", [0f, 1f, 0f], "B")
        ]);

        var hits = await store.SearchAsync("c", [1f, 0f, 0f], top: 2);
        Assert.Equal("a", hits[0].Id);
    }

    [Fact]
    public async Task Document_Ingestor_Chunks_Embeds_And_Stores()
    {
        var store = new InMemoryVectorStore();
        var ingestor = new DocumentIngestor(new NoOpEmbeddingGenerator(), store);
        var text = string.Join("\n\n",
            Enumerable.Range(0, 5).Select(i => string.Join(' ', Enumerable.Repeat($"w{i}", 100))));

        var count = await ingestor.IngestAsync("docs", "doc1", text);
        Assert.True(count > 0);

        var hits = await store.SearchAsync("docs", new float[384], top: 100);
        Assert.Equal(count, hits.Length);
    }
}
