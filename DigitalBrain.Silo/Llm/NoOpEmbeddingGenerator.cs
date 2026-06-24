using Microsoft.Extensions.AI;

namespace DigitalBrain.Silo.Llm;

// 384-dimension zero-vector embedding generator (ported from IAW). Registered fail-soft so the Context neuron's
// RAG is always wired; with NoOp the hybrid scorer detects the zero vectors and falls back to keyword recall.
// Swapping this for a real IEmbeddingGenerator (Ollama/OpenAI) activates vector scoring with no code change.
public sealed class NoOpEmbeddingGenerator(int dimensions = 384) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(_ => new Embedding<float>(new float[dimensions])).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
