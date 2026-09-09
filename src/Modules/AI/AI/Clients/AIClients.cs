using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI;

internal static class AIClients
{
    internal const string ConfigurationRoot = "DigitalBrain:AI";
    internal const string DefaultModelKey = $"{ConfigurationRoot}:Default:Model";
    internal const string DefaultEmbeddingKey = $"{ConfigurationRoot}:Default:Embedding";
    internal const string DefaultTranscriptionKey = $"{ConfigurationRoot}:Default:Transcription";
    internal const string DefaultImageKey = $"{ConfigurationRoot}:Default:Image";
    internal const string SensitiveTelemetryKey = $"{ConfigurationRoot}:Telemetry:EnableSensitiveData";
    private const string TelemetrySource = "DigitalBrain.AI";

    private static readonly IReadOnlyDictionary<AiProvider, ILlmProviderFactory> Factories =
        new ILlmProviderFactory[]
        {
            new OpenAIProviderFactory(),
            new AnthropicProviderFactory(),
            new GoogleProviderFactory(),
            new XAIProviderFactory(),
            new OllamaProviderFactory(),
        }.ToDictionary(static factory => factory.Provider);

    internal static void Add(IServiceCollection services)
    {
        services.TryAddSingleton<IUntrustedContentScreen, UntrustedContentScreen>();
        foreach (var model in LLMModel.All)
        {
            services.AddKeyedSingleton<IChatClient>(
                model.Marker,
                (provider, _) => BuildChatPipeline(provider, model));
        }

        foreach (var model in EmbeddingModel.All)
        {
            services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                model.Marker,
                (provider, _) => Factories[model.Provider].CreateEmbeddingGenerator(
                    model,
                    provider.GetRequiredService<IConfiguration>()));
        }

        services.TryAddSingleton(DefaultChatClient);
        services.TryAddSingleton(DefaultEmbeddingGenerator);

        services.AddHostedService<LlmWarmupHostedService>();
    }

    private static IChatClient BuildChatPipeline(IServiceProvider provider, LLMModel model)
        => BuildChatPipeline(provider, model, Factories[model.Provider].CreateChatClient(
            model, provider.GetRequiredService<IConfiguration>()));

    internal static IChatClient BuildChatPipeline(IServiceProvider provider, LLMModel model, IChatClient innerClient)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var captureContent = configuration.GetValue<bool?>(SensitiveTelemetryKey)
            ?? configuration.GetValue<bool?>("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT")
            ?? false;
        var loggerFactory = provider.GetService<ILoggerFactory>();
        var pipeline = new ChatClientBuilder(innerClient);
        if (!model.SupportsTools)
        {
            // Models that cannot emit tool calls must never be told about tools —
            // the assistant then answers capability questions honestly with "no".
            pipeline = pipeline.Use(static async (messages, options, next, cancellationToken) =>
            {
                if (options?.Tools is { Count: > 0 })
                {
                    options = options.Clone();
                    options.Tools = null;
                    options.ToolMode = null;
                }

                await next(messages, options, cancellationToken).ConfigureAwait(false);
            });
        }
        return pipeline
            .UseFunctionInvocation()
            .UseOpenTelemetry(
                loggerFactory: loggerFactory,
                sourceName: $"{TelemetrySource}.{model.Marker.Name}",
                configure: telemetry => telemetry.EnableSensitiveData = captureContent)
            .Build(provider);
    }

    private static IChatClient DefaultChatClient(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var model = configuration[DefaultModelKey] is { Length: > 0 } markerName
            ? LLMModel.FindByMarkerName(markerName)
                ?? throw UnknownMarker(DefaultModelKey, markerName, LLMModel.All.Select(static m => m.Marker.Name))
            : FirstConfiguredModel(configuration);
        return provider.GetRequiredKeyedService<IChatClient>(model.Marker);
    }

    private static LLMModel FirstConfiguredModel(IConfiguration configuration)
        => LLMModel.All.FirstOrDefault(model => Factories[model.Provider].IsConfigured(configuration))
            ?? throw new InvalidOperationException(
                $"No LLM provider is configured. Supply a provider API key (for example "
                + $"{ConfigurationRoot}:OpenAI:ApiKey) or an Ollama endpoint, or pin {DefaultModelKey}.");

    private static IEmbeddingGenerator<string, Embedding<float>> DefaultEmbeddingGenerator(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        if (configuration[DefaultEmbeddingKey] is { Length: > 0 } markerName)
        {
            var configured = EmbeddingModel.FindByMarkerName(markerName)
                ?? throw UnknownMarker(DefaultEmbeddingKey, markerName, EmbeddingModel.All.Select(static m => m.Marker.Name));
            return provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(configured.Marker);
        }

        // The default embedding stays pinned to the local model unless explicitly
        // configured: silently switching it changes vector dimensions and orphans
        // every existing Qdrant collection.
        var local = EmbeddingModel.All.Single(static model => model.Marker == typeof(Ollama.IEmbeddingGemma));
        if (!Factories[local.Provider].IsConfigured(configuration))
        {
            throw new InvalidOperationException(
                $"No embedding model is configured. Supply an Ollama endpoint for {local.Marker.Name}, "
                + $"or pin {DefaultEmbeddingKey} to a configured cloud embedding "
                + $"({string.Join(", ", EmbeddingModel.All.Select(static m => m.Marker.Name))}).");
        }

        return provider.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(local.Marker);
    }

    internal static void AddImageGeneration(IServiceCollection services, IConfiguration configuration)
    {
        // The marker names the model, as with chat and embeddings; an unpinned
        // key keeps the catalogue's first entry.
        var markerName = configuration[DefaultImageKey];
        var model = string.IsNullOrEmpty(markerName)
            ? ImageModel.All[0]
            : ImageModel.FindByMarkerName(markerName);

        // Resolution failure is deferred into the factory rather than thrown here:
        // this runs during service registration, so throwing would take the whole
        // silo down over a typo in a peripheral feature. Default:Model does the
        // same, and Default:Transcription degrades to a 503 with the reason.
        if (model is null)
        {
            services.AddSingleton<IImageGeneration>(_ => throw UnknownMarker(
                DefaultImageKey, markerName!, ImageModel.All.Select(static m => m.Marker.Name)));
            return;
        }

        if (configuration[$"{ConfigurationRoot}:{model.Provider}:ApiKey"] is { Length: > 0 })
        {
            services.AddSingleton<IImageGeneration>(sp =>
                new OpenAIImageGeneration(model, sp.GetRequiredService<IConfiguration>()));
        }
    }

    private static InvalidOperationException UnknownMarker(
        string configurationKey,
        string markerName,
        IEnumerable<string> knownMarkerNames)
        => new($"{configurationKey} names unknown model '{markerName}'. "
            + $"Known models: {string.Join(", ", knownMarkerNames)}.");
}
