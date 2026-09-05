using DigitalBrain.Core;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Sdk;
using DigitalBrain.Sdk.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubModule
{
    internal static void Configure(ISiloBuilder builder)
    {
        var bindings = GitHubRepositoryBindings.Read(builder.Configuration);
        builder.Services.AddSingleton(bindings);
        builder.Services.AddGitHubAuthentication(builder.Configuration);
        builder.Services.AddSingleton<GitHubInstallationTokens>();
        builder.Services.AddSingleton<IGitHubRepositorySource, GitHubRepositorySource>();
        builder.Services.AddSingleton<GitHubSetupService>();
        builder.Services.AddSingleton<IGitHubSetup>(services => services.GetRequiredService<GitHubSetupService>());
        builder.Services.AddHostedService<GitHubConnectionRecovery>();
        builder.Services.AddWebhookNeurons();
        builder.Services.AddSingleton<IWebhookProcessor, GitHubWebhookProcessor>();
        builder.Services.AddSingleton(new NeuronPresentation("repository", "GitHub Repository", "Microsoft", "github"));
        foreach (var binding in bindings.All)
        {
            binding.BeginRecovery();
            // Configured per-repository endpoints share the authenticated source implementation.
            builder.Services.AddSingleton<IHttpSurface>(services => new WebhookSurface(
                new WebhookDefinition($"/integrations/github/{binding.EndpointId}/webhook"),
                new GitHubWebhookHandler(binding, services.GetRequiredService<IGrainFactory>())));
        }
        // One App endpoint routes authenticated deliveries to every authorized repository scope.
        builder.Services.AddSingleton<IHttpSurface>(services => new WebhookSurface(
            new WebhookDefinition("/integrations/github/webhook"),
            new GitHubSharedWebhookHandler(bindings, services.GetRequiredService<IGrainFactory>())));
    }
}
