using System.ClientModel;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;

namespace DigitalBrain.AI;

/// <summary>The name of the provider whose client is used when a signal names none.</summary>
public sealed record AIDefaults(string DefaultProvider);

/// <summary>
/// Native tools contributed by other modules, keyed by the name an agent's
/// <c>Instruct.tools</c> uses. The four brain operations are not in here: every agent gets
/// those bound to its own identity.
/// </summary>
public sealed class NativeTools
{
    private readonly Dictionary<string, AIFunction> _functions = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, AIFunction> Functions => _functions;

    public void Add(string name, AIFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        _functions[name] = function;
    }

    /// <summary>
    /// The tools an <c>Instruct.tools</c> list names. A name no module registered is ignored:
    /// a host that dropped a module should not stop every agent that once used it.
    /// </summary>
    public IEnumerable<AIFunction> Resolve(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            if (_functions.TryGetValue(name, out var function))
            {
                yield return function;
            }
        }
    }
}

/// <summary>
/// Registers one keyed <see cref="IChatClient"/> per configured provider, under
/// <c>DigitalBrain:AI:Providers:{name}:{Endpoint,ApiKey,Model}</c>. A provider without an
/// API key is skipped; with no providers at all, <c>XAI_API_KEY</c> configures <c>xai</c>.
/// </summary>
public sealed class AIModule : IModule
{
    internal const string DefaultProviderName = "xai";
    internal const string DefaultEndpoint = "https://api.x.ai/v1";
    internal const string DefaultModel = "grok-4.6";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuration = builder.Configuration.GetSection("DigitalBrain:AI");
        var configured = 0;
        foreach (var provider in configuration.GetSection("Providers").GetChildren())
        {
            var key = provider["ApiKey"];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            Register(
                builder,
                provider.Key,
                provider["Endpoint"] ?? DefaultEndpoint,
                key,
                provider["Model"] ?? DefaultModel);
            configured++;
        }

        // A developer with only an environment key still gets a working brain.
        if (configured == 0)
        {
            var fallback = Environment.GetEnvironmentVariable("XAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                Register(builder, DefaultProviderName, DefaultEndpoint, fallback, DefaultModel);
            }
        }

        builder.Services.TryAddSingleton(new AIDefaults(configuration["DefaultProvider"] ?? DefaultProviderName));
        builder.Services.TryAddSingleton<NativeTools>();
    }

    private static void Register(ISiloBuilder builder, string name, string endpoint, string apiKey, string model)
        => builder.Services.AddKeyedSingleton<IChatClient>(name, (_, _) =>
            new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
                .GetChatClient(model)
                .AsIChatClient());
}
