using Microsoft.Extensions.AI;

namespace DigitalBrain.Silo;

// Paragraph-aware text chunking (~targetWords, hard cap maxWords). Harvested from IAW's PdfIngestionSource chunker.
public static class TextChunker
{
    public static IReadOnlyList<string> Chunk(string text, int targetWords = 200, int maxWords = 400)
    {
        var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var current = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            var words = paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (current.Count > 0 && current.Count + words.Length > maxWords)
                Flush(chunks, current);

            current.AddRange(words);
            if (current.Count >= targetWords)
                Flush(chunks, current);
        }

        if (current.Count > 0)
            chunks.Add(string.Join(' ', current));
        return chunks;
    }

    private static void Flush(List<string> chunks, List<string> current)
    {
        chunks.Add(string.Join(' ', current));
        current.Clear();
    }
}

// NOTE: PDF ingestion (UglyToad.PdfPig) is deferred — the configured NuGet feed has no stable PdfPig (only a
// custom prerelease). TextChunker + DocumentIngestor work on any text; a PDF text source slots in later.

// Ingests a document into a vector store: chunk -> embed -> ensure collection -> upsert. Embeddings come from the
// registered IEmbeddingGenerator (NoOp by default; real Ollama/OpenAI when configured).
public sealed class DocumentIngestor(IEmbeddingGenerator<string, Embedding<float>> embedder, IVectorStore store)
{
    public async Task<int> IngestAsync(string collection, string documentId, string text, CancellationToken ct = default)
    {
        var chunks = TextChunker.Chunk(text);
        if (chunks.Count == 0) return 0;

        var embeddings = await embedder.GenerateAsync(chunks, cancellationToken: ct);
        var dimension = embeddings.Count > 0 ? embeddings[0].Vector.Length : 384;
        await store.EnsureCollectionAsync(collection, dimension, ct);

        var records = chunks.Select((chunk, i) => new VectorRecord($"{documentId}#{i}", embeddings[i].Vector.ToArray(), chunk));
        await store.UpsertAsync(collection, records, ct);
        return chunks.Count;
    }
}
