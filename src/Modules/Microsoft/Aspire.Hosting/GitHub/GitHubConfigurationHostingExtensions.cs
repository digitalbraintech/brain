using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.Hosting;

public static class GitHubConfigurationHostingExtensions
{
    /// <summary>Opt in configured repositories while keeping App keys and HMAC secrets in Aspire parameters.</summary>
    public static DigitalBrainModuleBuilder<MicrosoftModule> WithConfiguredGitHubRepositories(
        this DigitalBrainModuleBuilder<MicrosoftModule> module, IConfiguration configuration)
    {
        module.WithConfiguredGitHubApp(configuration);
        foreach (var repository in configuration.GetSection("DigitalBrain:Microsoft:GitHub:Repositories").GetChildren())
        {
            module.WithGitHubRepository(repository.Key, Required(repository, "Owner"),
                Guid.Parse(Required(repository, "Principal")), Number(repository, "AppId"),
                Number(repository, "InstallationId"), Number(repository, "RepositoryId"),
                Required(repository, "RepoOwner"), Required(repository, "RepoName"),
                repository["EndpointId"], repository["ApiHost"] is { } api ? new Uri(api) : null,
                repository["McpEndpoint"] is { } mcp ? new Uri(mcp) : null);
        }
        return module;
    }

    public static DigitalBrainModuleBuilder<MicrosoftModule> WithConfiguredGitHubApp(
        this DigitalBrainModuleBuilder<MicrosoftModule> module, IConfiguration configuration)
    {
        var app = configuration.GetSection("DigitalBrain:Microsoft:GitHub:App");
        if (app.Exists())
        {
            module.WithGitHubApp(Number(app, "AppId"), Required(app, "Slug"), Required(app, "ClientId"),
                new Uri(Required(app, "PublicOrigin")), new Uri(Required(app, "PublicWebhookUrl")));
        }
        return module;
    }

    private static string Required(IConfiguration section, string name)
        => !string.IsNullOrWhiteSpace(section[name]) ? section[name]!
            : throw new InvalidOperationException($"The GitHub repository binding requires {name}.");

    private static long Number(IConfiguration section, string name)
        => long.Parse(Required(section, name), System.Globalization.CultureInfo.InvariantCulture);
}
