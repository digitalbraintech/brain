using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.AI.Tests;

public sealed class AIHostingExtensionsTests
{
    [Fact]
    public void Tavily_is_absent_until_enabled()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain").AddModule<AIModule>();
        builder.AddExecutable("consumer", "dotnet", ".").WithReference(brain);

        Assert.DoesNotContain(builder.Resources.OfType<ParameterResource>(), resource => resource.Name == "tavily-api-key");
    }

    [Fact]
    public async Task Tavily_registration_is_idempotent_describes_and_projects_the_secret()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.Configuration["Parameters:tavily-api-key"] = "sentinel-key";
        var brain = builder.AddDigitalBrain("brain").AddModule<AIModule>(ai =>
        {
            ai.WithTavilySearch();
            ai.WithTavilySearch();
        });
        var consumer = builder.AddExecutable("consumer", "dotnet", ".").WithReference(brain);

        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>(), resource => resource.Name == "tavily-api-key");
        Assert.True(parameter.Secret);
        Assert.Null(parameter.Default);
        Assert.Contains("API key for Tavily web search", parameter.Description, StringComparison.Ordinal);
        Assert.Contains("[Tavily](https://www.tavily.com/)", parameter.Description, StringComparison.Ordinal);
        Assert.Contains("account dashboard", parameter.Description, StringComparison.Ordinal);
        Assert.True(parameter.EnableDescriptionMarkdown);
        var environment = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            consumer.Resource,
            environment,
            TestContext.Current.CancellationToken);
        foreach (var annotation in consumer.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }
        Assert.Equal("true", environment["DigitalBrain__AI__Tavily__Enabled"]);
        Assert.Same(parameter, environment["DigitalBrain__AI__Tavily__ApiKey"]);
    }
}

