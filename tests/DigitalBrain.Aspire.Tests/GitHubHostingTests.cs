using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Microsoft;
using DigitalBrain.Microsoft.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

public sealed class GitHubHostingTests
{
    [Fact]
    public async Task Shared_app_setup_projects_metadata_and_secret_parameters_only_to_kernel()
    {
        const string section = "DigitalBrain:Microsoft:GitHub:App:";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [section + "AppId"] = "42", [section + "Slug"] = "digitalbrain", [section + "ClientId"] = "Iv1.fixture",
            [section + "PublicOrigin"] = "https://brain.example/", [section + "PublicWebhookUrl"] = "https://hooks.example/integrations/github/webhook",
        }).Build();
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var brain = builder.AddDigitalBrain("github-app")
            .AddModule<MicrosoftModule>(module => module.WithConfiguredGitHubRepositories(configuration));
        var kernel = builder.AddExecutable("kernel", "dotnet", ".").WithReference(brain);
        var client = builder.AddExecutable("client", "dotnet", ".").WithReference(brain.AsClient());
        await using var app = builder.Build();
        var environment = await RenderAsync(kernel.Resource);
        var clientEnvironment = await RenderAsync(client.Resource);
        const string root = "DigitalBrain__Microsoft__GitHub__App__";
        Assert.Equal("42", environment[root + "AppId"]);
        Assert.Equal("https://brain.example/", environment[root + "PublicOrigin"]);
        Assert.Contains(root + "PrivateKeyPem", environment.Keys);
        Assert.Contains(root + "WebhookSecret", environment.Keys);
        Assert.Contains(root + "ClientSecret", environment.Keys);
        Assert.DoesNotContain(clientEnvironment.Keys, key => key.StartsWith(root, StringComparison.Ordinal));
        Assert.Equal(3, builder.Resources.OfType<ParameterResource>().Count(parameter => parameter.Name.StartsWith("github-app-", StringComparison.Ordinal) && parameter.Secret));
    }

    [Fact]
    public async Task Apphost_metadata_configuration_uses_private_parameters_for_keys()
    {
        var principal = Guid.NewGuid();
        const string section = "DigitalBrain:Microsoft:GitHub:Repositories:configured:";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [section + "Owner"] = "dev", [section + "Principal"] = principal.ToString(),
            [section + "AppId"] = "1", [section + "InstallationId"] = "2", [section + "RepositoryId"] = "3",
            [section + "RepoOwner"] = "owner", [section + "RepoName"] = "repo", [section + "EndpointId"] = "route-id",
        }).Build();
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var brain = builder.AddDigitalBrain("github-configured")
            .AddModule<MicrosoftModule>(module => module.WithConfiguredGitHubRepositories(configuration));
        var kernel = builder.AddExecutable("kernel", "dotnet", ".").WithReference(brain);
        await using var app = builder.Build();
        var environment = await RenderAsync(kernel.Resource);
        Assert.Equal("route-id", environment["DigitalBrain__Microsoft__GitHub__Repositories__configured__EndpointId"]);
        Assert.Contains(builder.Resources.OfType<ParameterResource>(), parameter => parameter.Name == "github-configured-app-private-key" && parameter.Secret);
        Assert.Contains(builder.Resources.OfType<ParameterResource>(), parameter => parameter.Name == "github-configured-webhook-secret" && parameter.Secret);
    }

    [Fact]
    public async Task Repository_configuration_and_private_parameters_project_only_to_kernel()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var principal = Guid.NewGuid();
        var brain = builder.AddDigitalBrain("github-test")
            .AddModule<MicrosoftModule>(module =>
            {
                module.WithGitHubRepository("first", "dev", principal, 1, 2, 3, "owner", "repo", "opaque-endpoint");
                module.WithGitHubRepository("second", "dev", principal, 1, 2, 4, "owner", "another-repo");
            });
        var kernel = builder.AddExecutable("kernel", "dotnet", ".").WithReference(brain);
        var client = builder.AddExecutable("client", "dotnet", ".").WithReference(brain.AsClient());
        await using var app = builder.Build();
        var environment = await RenderAsync(kernel.Resource);
        var clientEnvironment = await RenderAsync(client.Resource);
        const string root = "DigitalBrain__Microsoft__GitHub__Repositories__";
        Assert.Equal("3", environment[root + "first__RepositoryId"]);
        Assert.Equal("4", environment[root + "second__RepositoryId"]);
        Assert.Equal(principal.ToString("D"), environment[root + "first__Principal"]);
        Assert.Equal("opaque-endpoint", environment[root + "first__EndpointId"]);
        Assert.Contains(root + "first__PrivateKeyPem", environment.Keys);
        Assert.Contains(root + "first__WebhookSecret", environment.Keys);
        Assert.DoesNotContain(clientEnvironment.Keys, key => key.StartsWith(root, StringComparison.Ordinal));
        var secrets = builder.Resources.OfType<ParameterResource>().Where(item => item.Name.StartsWith("github-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, secrets.Length);
        Assert.All(secrets, secret => Assert.True(secret.Secret));
        Assert.DoesNotContain(environment.Keys, key => key.StartsWith("DigitalBrain__Microsoft__Aspire", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyDictionary<string, string>> RenderAsync(IResource resource)
    {
        var configuration = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
            .BuildAsync(new(DistributedApplicationOperation.Publish), NullLogger.Instance);
        return configuration.EnvironmentVariables.ToDictionary();
    }
}
