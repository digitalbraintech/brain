using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.AI.Aspire.Hosting;

/// <summary>An LLM endpoint the brain may talk to. The API key is an Aspire secret parameter.</summary>
public sealed record AIProvider(string Name, string Endpoint, string Model);

public sealed class AIHostingOptions
{
    private readonly List<AIProvider> _providers = [];

    /// <summary>The provider used when a signal names none. Defaults to the first provider added.</summary>
    public string? DefaultProvider { get; set; }

    public IReadOnlyList<AIProvider> Providers => _providers;

    public AIHostingOptions AddProvider(string name, string endpoint, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _providers.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.Ordinal));
        _providers.Add(new AIProvider(name, endpoint, model));
        return this;
    }
}

public static class AIHostingExtensions
{
    private const string DefaultProviderName = "xai";
    private const string DefaultEndpoint = "https://api.x.ai/v1";
    private const string DefaultModel = "grok-4.6";

    /// <summary>
    /// Adds the AI module to the brain. Out of the box that is one provider — xAI's
    /// OpenAI-compatible endpoint running grok-4.6 — whose key comes from the
    /// <c>ai-xai-apikey</c> parameter (user secrets in development, a real secret in publish).
    /// </summary>
    public static DigitalBrainBuilder AddAI(this DigitalBrainBuilder brain, Action<AIHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(brain);

        var options = new AIHostingOptions();
        configure?.Invoke(options);
        if (options.Providers.Count == 0)
        {
            options.AddProvider(DefaultProviderName, DefaultEndpoint, DefaultModel);
        }

        var keys = new Dictionary<string, IResourceBuilder<ParameterResource>>(StringComparer.Ordinal);
        foreach (var provider in options.Providers)
        {
            keys[provider.Name] = brain.ApplicationBuilder.AddParameter($"ai-{provider.Name}-apikey", secret: true);
        }

        var defaultProvider = options.DefaultProvider ?? options.Providers[0].Name;
        return brain.AddModule<AIModule>(module =>
            module.AddProjection(new AIProjection(defaultProvider, options.Providers, keys)));
    }

    private sealed class AIProjection(
        string defaultProvider,
        IReadOnlyList<AIProvider> providers,
        IReadOnlyDictionary<string, IResourceBuilder<ParameterResource>> keys)
        : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.WithEnvironment("DigitalBrain__AI__DefaultProvider", defaultProvider);
            foreach (var provider in providers)
            {
                builder
                    .WithEnvironment($"DigitalBrain__AI__Providers__{provider.Name}__Endpoint", provider.Endpoint)
                    .WithEnvironment($"DigitalBrain__AI__Providers__{provider.Name}__Model", provider.Model)
                    .WithEnvironment($"DigitalBrain__AI__Providers__{provider.Name}__ApiKey", keys[provider.Name]);
            }
        }
    }
}
