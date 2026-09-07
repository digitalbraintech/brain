using System.Text.Json;
using System.Net.Http.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class ApplicationCapabilityCatalogTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(), "digitalbrain-capability-catalog-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Installed_modules_publish_a_finite_authored_application_catalog_without_a_saved_application()
    {
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddApplicationAuthoring(storeRoot),
        });

        var actor = new ActorContext(PrincipalId.New(), "author");
        using var verified = VerifiedActor.Enter(actor);
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "catalog-owner", actor);
        var authoring = simulation.GetSiloService<IApplicationAuthoring>();
        var catalog = await authoring.CatalogAsync(
            brain, TestContext.Current.CancellationToken);

        var agent = Assert.Single(catalog.Neurons, capability => capability.Contract == "DigitalBrain.AI.IAgent");
        Assert.Equal("agent", agent.Key);
        Assert.EndsWith("DigitalBrain.Modules.AI.Sdk.csproj", agent.ProjectReference, StringComparison.Ordinal);
        var request = Assert.Single(agent.Inputs, input => input.Key == "request");
        Assert.Equal("db.agent-request/v1", request.Request.Name);
        Assert.Equal("DigitalBrain.AI.AgentRequest", request.Request.TypeName);
        Assert.Equal("db.agent-reply/v1", request.Response!.Name);
        AssertObjectSchema(request.Request);
        AssertObjectSchema(request.Response);
        using (var requestSchema = JsonDocument.Parse(request.Request.JsonSchema!))
        {
            var properties = requestSchema.RootElement.GetProperty("properties");
            Assert.True(properties.TryGetProperty("Text", out _));
            Assert.False(properties.TryGetProperty("text", out _));
        }
        Assert.Null(request.Request.ExampleJson);
        Assert.Null(request.Response.ExampleJson);

        var renderer = Assert.Single(
            catalog.Neurons, capability => capability.Contract == "DigitalBrain.UI.IUIRenderer");
        Assert.Equal("default", renderer.DefaultInstanceName);
        Assert.EndsWith("DigitalBrain.Modules.UI.Contracts.csproj", renderer.ProjectReference,
            StringComparison.Ordinal);
        var open = Assert.Single(renderer.Inputs, input => input.Key == "open-surface");
        Assert.Equal("ui.open-surface/v1", open.Request.Name);
        Assert.Equal("DigitalBrain.UI.OpenSurface", open.Request.TypeName);
        Assert.Null(open.Response);
        AssertObjectSchema(open.Request);
        Assert.Null(open.Request.ExampleJson);
        var added = Assert.Single(renderer.Events, output => output.Key == "component-added");
        Assert.Equal("ui.component-added/v1", added.Payload.Name);
        AssertObjectSchema(added.Payload);
        Assert.Null(added.Payload.ExampleJson);

        Assert.DoesNotContain(catalog.Neurons,
            capability => capability.Contract.Contains("Kernel", StringComparison.Ordinal));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton(authoring);
        await using var server = builder.Build();
        server.Use(async (_, next) =>
        {
            using var scope = VerifiedActor.Enter(actor);
            await next();
        });
        server.MapApplicationStudio();
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var http = server.GetTestClient();
        var remote = await http.GetFromJsonAsync<ApplicationCapabilityCatalog>(
            "/applications/catalog", TestContext.Current.CancellationToken);
        Assert.Contains(remote!.Neurons, capability => capability.Contract == "DigitalBrain.AI.IAgent");
    }

    private static void AssertObjectSchema(ApplicationJsonContract contract)
    {
        using var schema = JsonDocument.Parse(Assert.IsType<string>(contract.JsonSchema));
        Assert.Equal(["object", "null"], schema.RootElement.GetProperty("type")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(storeRoot))
        {
            Directory.Delete(storeRoot, recursive: true);
        }
    }
}
