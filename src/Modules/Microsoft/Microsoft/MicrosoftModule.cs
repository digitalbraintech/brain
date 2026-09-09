using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Microsoft.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft;

public sealed class MicrosoftModule : IModule
{
    public const string AspireConfigurationRoot = "DigitalBrain:Microsoft:Aspire";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureAspire(builder);
        GitHubModule.Configure(builder);
    }

    private static void ConfigureAspire(ISiloBuilder builder)
    {
        builder.Services.AddSingleton(new NeuronPresentation("aspire", "Aspire", "Microsoft", "aspire"));
        var configuration = builder.Configuration.GetSection(AspireConfigurationRoot);
        if (DigitalBrainFakes.Enabled(builder.Configuration) || string.IsNullOrWhiteSpace(configuration["ProjectPath"]))
        {
            return;
        }

        var project = Path.GetFullPath(configuration["ProjectPath"]!);
        if (!File.Exists(project))
        {
            throw new InvalidOperationException("The configured Aspire AppHost project does not exist.");
        }

        var settings = new AspireConnectionSettings(
            project,
            configuration["ApplicationName"] ?? "DigitalBrain",
            configuration["Alias"] ?? "digitalbrain-local",
            new OwnerId(configuration["Owner"] ?? throw new InvalidOperationException("An owner must be configured for the Aspire connection.")),
            configuration["Command"] ?? "aspire");
        _ = PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), settings.Alias);

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(static services => new AspireConnection(
            services.GetRequiredService<AspireConnectionSettings>(), services.GetRequiredService<IUntrustedContentScreen>()));
        builder.Services.AddSingleton<IAgentToolSource>(new AgentDelegation<IAspire>(
            "ask_aspire",
            $"Ask the Aspire infrastructure specialist about the live {settings.ApplicationName} application. "
                + "Use for current service/resource status, health, errors, logs, distributed traces and diagnosing failed requests. "
                + $"The configured agent instance is <current-principal>.{settings.Alias}. It uses its own Aspire MCP tools and returns observed evidence. "
                + "Pass the question and relevant resource names or trace IDs. Read-only; no restart or deployment.",
            settings.Alias, settings.Owner));
    }
}

internal sealed record AspireConnectionSettings(string ProjectPath, string ApplicationName, string Alias, OwnerId Owner, string Command);
