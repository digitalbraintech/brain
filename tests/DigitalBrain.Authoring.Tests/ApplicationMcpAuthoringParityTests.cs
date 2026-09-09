using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class ApplicationMcpAuthoringParityTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(), "db-mcp-authoring-parity", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Mcp_uses_the_shared_HTTP_authoring_surface_for_files_catalog_and_scenarios()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            UseExternalGateway = true,
            ConfigureSilo = silo => silo.Services.AddApplicationAuthoring(storeRoot),
        });
        var actor = new ActorContext(PrincipalId.New(), "mcp-author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "mcp-owner", actor);
        var authoring = simulation.GetSiloService<IApplicationAuthoring>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton(authoring);
        await using var server = builder.Build();
        server.Use(async (_, next) =>
        {
            using var verified = VerifiedActor.Enter(actor);
            await next();
        });
        server.MapApplicationStudio();
        await server.StartAsync(ct);
        using var http = server.GetTestClient();
        var tools = new GraphTools(brain, new ApplicationAuthoringHttpClient(http));

        using var catalog = JsonDocument.Parse(await tools.ApplicationCatalogAsync(ct));
        Assert.Empty(catalog.RootElement.GetProperty("neurons").EnumerateArray());

        var saved = JsonSerializer.Deserialize<ApplicationSource>(
            await tools.SaveApplicationAsync("ping", SourceFor("ping"), null, ct), JsonSerializerOptions.Web)!;
        saved = JsonSerializer.Deserialize<ApplicationSource>(await tools.SaveApplicationFileAsync(
            "ping", "acceptance.json", JsonSerializer.Serialize(new
            {
                instruction = "Reply pong to /ping.",
                examples = new[] { new { name = "ping", operation = "reply", inputJson = "\"/ping\"", expectedJson = "\"pong\"" } },
            }), saved.SourceRevision, ct), JsonSerializerOptions.Web)!;

        var expectations = JsonSerializer.Deserialize<ApplicationExpectations>(
            await tools.SetApplicationExpectationsAsync("ping", saved.Source, Guid.NewGuid(), null, ct), JsonSerializerOptions.Web)!;
        var savedExpectations = JsonSerializer.Deserialize<ApplicationExpectations>(
            await tools.ReadApplicationExpectationsAsync("ping", expectations.ExpectationRevision, ct), JsonSerializerOptions.Web)!;
        Assert.Equal(saved.Source, savedExpectations.DocumentJson);
        using var files = JsonDocument.Parse(await tools.ListApplicationFilesAsync("ping", ct));
        Assert.Equal(["acceptance.json", "application.cs"],
            files.RootElement.EnumerateArray().Select(static item => item.GetString()).ToArray());
        using var acceptance = JsonDocument.Parse(
            await tools.ReadApplicationFileAsync("ping", "acceptance.json", ct));
        Assert.Equal(saved.SourceRevision, acceptance.RootElement.GetProperty("sourceRevision").GetString());
        using var listed = JsonDocument.Parse(await tools.ListApplicationsAsync(ct));
        Assert.Equal("ping", Assert.Single(listed.RootElement.EnumerateArray()).GetProperty("key").GetString());
        using var read = JsonDocument.Parse(await tools.ReadApplicationAsync("ping", ct));
        Assert.Equal(saved.SourceRevision, read.RootElement.GetProperty("sourceRevision").GetString());

        var validation = JsonSerializer.Deserialize<ApplicationValidation>(
            await tools.ValidateApplicationAsync("ping", saved.SourceRevision, ct), JsonSerializerOptions.Web)!;
        Assert.True(validation.Succeeded, validation.Diagnostics);
        var report = JsonSerializer.Deserialize<ApplicationScenarioRun>(
            await tools.RunApplicationScenariosAsync("ping", saved.SourceRevision, ct), JsonSerializerOptions.Web)!;
        Assert.True(report.Passed);
        Assert.Equal(expectations.ExpectationRevision, report.ExpectationRevision);
        var retained = JsonSerializer.Deserialize<ApplicationScenarioRun>(
            await tools.ReadApplicationScenariosAsync("ping", saved.SourceRevision, ct), JsonSerializerOptions.Web)!;
        Assert.Equal(report.SourceRevision, retained.SourceRevision);
        Assert.Equal(report.ArtifactRevision, retained.ArtifactRevision);
        Assert.True(retained.Passed);
        Assert.Equal(Assert.Single(report.Examples).ActualJson, Assert.Single(retained.Examples).ActualJson);
    }

    private static string SourceFor(string key)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var sdk = Path.Combine(directory!.FullName, "src", "Kernel", "DigitalBrain.Sdk",
            "DigitalBrain.Sdk.csproj").Replace('\\', '/');
        return $$"""
            #:project {{sdk}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false
            using DigitalBrain.Abstractions;
            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var app = brain.Application("{{key}}");
            app.Command<string, string>("reply", (_, _, _) => Task.FromResult("pong"));
            await app.RunAsync(args);
            """;
    }

    public void Dispose()
    {
        if (Directory.Exists(storeRoot))
        {
            Directory.Delete(storeRoot, recursive: true);
        }
    }
}
