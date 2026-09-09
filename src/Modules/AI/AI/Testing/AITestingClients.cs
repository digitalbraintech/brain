using Microsoft.Extensions.AI;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

// Testing-mode counterpart of AIClients: every model marker resolves to one
// deterministic responder so suites run offline without provider credentials.
internal static class AITestingClients
{
    internal static void Add(IServiceCollection services)
    {
        // Mirrors AIClients.BuildChatPipeline's .UseFunctionInvocation() wrapping: without it,
        // TestChatClient's scripted FunctionCallContent (render_chart/generate_image) would
        // never actually run the tool or come back around for the follow-up round, so kit
        // cards would never appear even in testing mode.
        var chatClient = new ChatClientBuilder(new TestChatClient()).UseFunctionInvocation().Build();
        var embeddingGenerator = new TestEmbeddingGenerator();

        foreach (var model in LLMModel.All)
        {
            services.AddKeyedSingleton<IChatClient>(model.Marker, chatClient);
        }

        foreach (var model in EmbeddingModel.All)
        {
            services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                model.Marker,
                embeddingGenerator);
        }

        services.TryAddSingleton<IChatClient>(chatClient);
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
        services.TryAddSingleton<IImageGeneration, TestImageGeneration>();
        services.TryAddSingleton<IUntrustedContentScreen, TestContentScreen>();

        // Voice too, and for the same reason: testing mode must not reach a real
        // provider. Registered here rather than through VoiceToTextHosting so no
        // pinned marker can route a suite at a billed endpoint.
        services.TryAddSingleton<IAudioConverter, OggOpusToWavConverter>();
        services.TryAddSingleton<IAudioTranscriptionService, TestTranscriptionService>();
    }

    // Testing mode composes offline provider fixtures and must never create a live classifier.
    private sealed class TestContentScreen : IUntrustedContentScreen
    {
        public Task ScreenAsync(string content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class TestEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embeddings = values.Select(value =>
                new Embedding<float>(
                    new[] { value.Length, value.Aggregate(0f, (sum, character) => sum + character) }));
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
