using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

public static class AIHostingExtensions
{
    private const string TavilyApiKeyEnvironmentKey = "DigitalBrain__AI__Tavily__ApiKey";
    private const string TavilyEnabledEnvironmentKey = "DigitalBrain__AI__Tavily__Enabled";
    private const string TavilyApiKeyDescription = "API key for Tavily web search. Sign up at [Tavily](https://www.tavily.com/) and copy your API key from your account dashboard. Paste it here to enable web search for DigitalBrain agents.";
    private const string EnableSensitiveDataEnvironmentKey =
        "DigitalBrain__AI__Telemetry__EnableSensitiveData";

    extension(DigitalBrainModuleBuilder<AIModule> module)
    {
        public bool EnableSensitiveData
        {
            get => State(module).EnableSensitiveData;
            set => State(module).EnableSensitiveData = value;
        }
    }

    public static DigitalBrainModuleBuilder<AIModule> WithLlm<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : ILLM
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).AddLlm(typeof(TModel));
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithEmbedding<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : IEmbedding
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).AddEmbedding(typeof(TModel));
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithDefaultLlm<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : ILLM
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).SetDefaultLlm(typeof(TModel));
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithDefaultEmbedding<TModel>(this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : IEmbedding
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).SetDefaultEmbedding(typeof(TModel));
        return module;
    }

    // Speech-to-text, local or hosted. ITranscription constrains the marker, so a
    // marker of the wrong kind is a compile error rather than a runtime lookup miss.
    public static DigitalBrainModuleBuilder<AIModule> WithVoiceToText<TModel>(
        this DigitalBrainModuleBuilder<AIModule> module)
        where TModel : ITranscription
    {
        ArgumentNullException.ThrowIfNull(module);
        var marker = typeof(TModel);
        var whisper = TranscriptionModel.FindByMarker(marker)
            ?? throw new NotSupportedException(
                $"{marker.FullName} is not a catalogued transcription model. "
                + $"Known models: {string.Join(", ", TranscriptionModel.All.Select(static m => m.Marker.Name))}.");

        var voice = module.Brain.GetOrAddState(static brain => new VoiceToTextHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(voice);
        }

        // A hosted transcription model needs its provider key like any other model.
        // WithLlm/WithEmbedding get this through AddModel; voice has its own
        // projection, so without this an AppHost that pins a hosted model without
        // also registering a chat model of the same provider ships no key, and the
        // endpoint answers 503 forever with no host-side way to fix it.
        if (!whisper.IsLocal)
        {
            State(module).EnsureProviderApiKey(whisper.Provider);
        }

        voice.SetModel(whisper);
        return module;
    }

    public static DigitalBrainModuleBuilder<AIModule> WithTavilySearch(
        this DigitalBrainModuleBuilder<AIModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);
        State(module).EnableTavilySearch();
        return module;
    }

    private static AIHostingState State(DigitalBrainModuleBuilder<AIModule> module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var state = module.Brain.GetOrAddState(static brain => new AIHostingState(brain), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class AIHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private const string OllamaImageTag = "latest";

        private readonly HashSet<Type> _markers = [];
        private readonly Dictionary<Type, IResourceBuilder<OllamaModelResource>> _ollamaModels = [];
        private readonly Dictionary<AiProvider, IResourceBuilder<ParameterResource>> _providerApiKeys = [];
        private IResourceBuilder<OllamaResource>? _ollama;
        private Type? _defaultLlmMarker;
        private Type? _defaultEmbeddingMarker;
        private IResourceBuilder<ParameterResource>? _tavilyApiKey;

        internal bool EnableSensitiveData { get; set; }

        internal void AddLlm(Type marker)
        {
            var model = LLMModel.FindByMarker(marker)
                ?? throw new NotSupportedException(
                    $"{marker.FullName} is not a known LLM model marker. Add it to LLMModel.All first.");

            AddModel(marker, model.Provider, model.Id);
        }

        internal void AddEmbedding(Type marker)
        {
            var model = EmbeddingModel.FindByMarker(marker)
                ?? throw new NotSupportedException(
                    $"{marker.FullName} is not a known embedding model marker. Add it to EmbeddingModel.All first.");

            AddModel(marker, model.Provider, model.Id);
        }

        internal void SetDefaultLlm(Type marker)
        {
            RequireAdded(marker);
            _defaultLlmMarker = marker;
        }

        internal void SetDefaultEmbedding(Type marker)
        {
            RequireAdded(marker);
            _defaultEmbeddingMarker = marker;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.WithEnvironment(
                EnableSensitiveDataEnvironmentKey,
                EnableSensitiveData ? "true" : "false")
                // Keep standard SDK instrumentation and the module pipeline on one
                // explicit host setting; an explicit false overrides ambient opt-ins.
                .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT",
                    EnableSensitiveData ? "true" : "false");

            if (_tavilyApiKey is not null)
            {
                builder
                    .WithEnvironment(TavilyEnabledEnvironmentKey, "true")
                    .WithEnvironment(TavilyApiKeyEnvironmentKey, _tavilyApiKey);
            }

            foreach (var (marker, resource) in _ollamaModels)
            {
                builder
                    .WithAnnotation(new WaitAnnotation(resource.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                    .WithEnvironment("DigitalBrain__AI__Ollama__Endpoint", resource.Resource.Parent.UriExpression)
                    .WithEnvironment($"DigitalBrain__AI__Ollama__{marker.Name}__Model", resource.Resource.ModelName);
            }

            foreach (var (provider, apiKey) in _providerApiKeys)
            {
                builder.WithEnvironment($"DigitalBrain__AI__{provider}__ApiKey", apiKey);
            }

            if (_defaultLlmMarker is { } llmMarker)
            {
                builder.WithEnvironment("DigitalBrain__AI__Default__Model", llmMarker.Name);
            }

            if (_defaultEmbeddingMarker is { } embeddingMarker)
            {
                builder.WithEnvironment("DigitalBrain__AI__Default__Embedding", embeddingMarker.Name);
            }
        }

        private void AddModel(Type marker, AiProvider provider, string id)
        {
            if (!_markers.Add(marker))
            {
                throw new InvalidOperationException(
                    $"{marker.FullName} is already configured on brain '{brain.Name}'. Add each model exactly once.");
            }

            if (provider == AiProvider.Ollama)
            {
                _ollamaModels[marker] = EnsureOllama().AddModel(OllamaResourceName(id), id);
            }
            else
            {
                EnsureProviderApiKey(provider);
            }
        }

        private void RequireAdded(Type marker)
        {
            if (!_markers.Contains(marker))
            {
                throw new InvalidOperationException(
                    $"{marker.Name} must be added with WithLlm/WithEmbedding before it can become the default.");
            }
        }

        internal void EnsureProviderApiKey(AiProvider provider)
        {
            if (_providerApiKeys.ContainsKey(provider))
            {
                return;
            }

            var apiKey = brain.ApplicationBuilder.AddParameter(
                $"{provider.ToString().ToLowerInvariant()}-api-key",
                secret: true);

            if (provider is AiProvider.OpenAI)
            {
                apiKey.WithDescription(
                    "Create an API key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys).",
                    enableMarkdown: true);
            }

            _providerApiKeys[provider] = apiKey;
        }

        internal void EnableTavilySearch()
        {
            _tavilyApiKey ??= brain.ApplicationBuilder
                .AddParameter("tavily-api-key", secret: true)
                .WithDescription(TavilyApiKeyDescription, enableMarkdown: true);
        }

        private static string OllamaResourceName(string id)
            => id.ToLowerInvariant().Replace(':', '-').Replace('.', '-').Replace('/', '-');

        private IResourceBuilder<OllamaResource> EnsureOllama()
            => _ollama ??= brain.ApplicationBuilder
                .AddOllama("ollama")
                .WithImageTag(OllamaImageTag)
                .WithGPUSupport()
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .WithEnvironment("OLLAMA_KEEP_ALIVE", "-1")
                .WithParentRelationship(brain.Resource);
    }

    private sealed class VoiceToTextHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        // One key for every transcription model, matching Default__Model and
        // Default__Embedding. The provider on the model picks the implementation.
        private const string TranscriptionEnvironmentKey = "DigitalBrain__AI__Default__Transcription";
        private TranscriptionModel? _model;

        internal void SetModel(TranscriptionModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (_model is not null && !string.Equals(_model.Id, model.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Voice-to-text is already configured on brain '{brain.Name}' as '{_model.Id}'. "
                    + "Call WithVoiceToText once.");
            }

            _model = model;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (_model is null)
            {
                return;
            }

            builder.WithEnvironment(TranscriptionEnvironmentKey, _model.Marker.Name);
        }
    }
}
