using System.Net.Http.Json;
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

public sealed class ApplicationAuthoringSurfaceTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(), "db-authoring-surfaces", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 30000)]
    public async Task Studio_HTTP_edits_child_files_in_the_same_revision_as_the_entry_file()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "surface-owner", actor);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton<IApplicationAuthoring>(new ApplicationAuthoringService(storeRoot));
        await using var server = builder.Build();
        server.Use(async (_, next) =>
        {
            using var scope = VerifiedActor.Enter(actor);
            await next();
        });
        server.MapApplicationStudio();
        await server.StartAsync(ct);
        using var http = server.GetTestClient();
        var savedResponse = await http.PostAsJsonAsync("/applications/start/save",
            new { source = "// entry", expectedRevision = (string?)null }, ct);
        savedResponse.EnsureSuccessStatusCode();
        var saved = (await savedResponse.Content.ReadFromJsonAsync<ApplicationSource>(ct))!;
        var missingReport = await http.GetAsync(
            $"/applications/start/scenarios?expectedSourceRevision={saved.SourceRevision}", ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missingReport.StatusCode);
        var editedResponse = await http.PostAsJsonAsync("/applications/start/file",
            new { path = "ui.cs", source = "// home", expectedRevision = saved.SourceRevision }, ct);
        editedResponse.EnsureSuccessStatusCode();
        var edited = (await editedResponse.Content.ReadFromJsonAsync<ApplicationSource>(ct))!;
        Assert.NotEqual(saved.SourceRevision, edited.SourceRevision);
        Assert.Equal("// home", edited.Source);
        IApplicationAuthoring remote = new ApplicationAuthoringHttpClient(http);
        Assert.Equal(["application.cs", "ui.cs"], await remote.ListFilesAsync(brain, "start", ct));
        var child = (await http.GetFromJsonAsync<ApplicationSource>("/applications/start/file?path=ui.cs", ct))!;
        Assert.Equal("// home", child.Source);
        var entry = (await http.GetFromJsonAsync<ApplicationSource>("/applications/start", ct))!;
        Assert.Equal("// entry", entry.Source);
        Assert.Equal(edited.SourceRevision, entry.SourceRevision);
    }

    [Fact(Timeout = 60000)]
    public async Task Studio_HTTP_and_MCP_client_share_the_silo_owned_application_store()
    {
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            UseExternalGateway = true,
            ConfigureSilo = silo => silo.Services.AddApplicationAuthoring(storeRoot),
        });
        var actor = new ActorContext(PrincipalId.New(), "authenticated-author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "surface-owner", actor);
        var authoring = simulation.GetSiloService<DigitalBrain.Abstractions.Scripting.IApplicationAuthoring>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton(authoring);
        await using var siloHttp = builder.Build();
        siloHttp.Use(async (_, next) =>
        {
            using var verifiedRequest = VerifiedActor.Enter(actor);
            await next();
        });
        siloHttp.MapApplicationStudio();
        await siloHttp.StartAsync(TestContext.Current.CancellationToken);
        using var http = siloHttp.GetTestClient();

        var saveResponse = await http.PostAsJsonAsync("/applications/ping/save", new
        {
            source = SourceFor("ping", "surface-pong"),
            expectedRevision = (string?)null,
        }, TestContext.Current.CancellationToken);
        saveResponse.EnsureSuccessStatusCode();
        var saved = await saveResponse.Content.ReadFromJsonAsync<ApplicationSource>(
            TestContext.Current.CancellationToken) ?? throw new InvalidOperationException("Save returned no source revision.");

        IApplicationAuthoring remote = new ApplicationAuthoringHttpClient(http);
        saved = await remote.SaveFileAsync(brain, "ping", "acceptance.json",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                instruction = "Reply surface-pong to /ping.",
                examples = new[] { new { name = "ping", operation = "reply", inputJson = "\"/ping\"", expectedJson = "\"surface-pong\"" } },
            }), saved.SourceRevision, TestContext.Current.CancellationToken);
        var validation = await remote.ValidateAsync(
            brain, "ping", saved.SourceRevision, TestContext.Current.CancellationToken);
        Assert.True(validation.Succeeded, validation.Diagnostics);
        var description = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/applications/ping/description?expectedSourceRevision={saved.SourceRevision}",
            TestContext.Current.CancellationToken);
        Assert.Equal("reply", Assert.Single(description.GetProperty("operations").EnumerateArray())
            .GetProperty("key").GetString());
        var report = await remote.RunScenariosAsync(brain, "ping", saved.SourceRevision, TestContext.Current.CancellationToken);
        Assert.True(report.Passed);
        Assert.Equal(report.SourceRevision, (await remote.ReadScenarioRunAsync(
            brain, "ping", saved.SourceRevision, TestContext.Current.CancellationToken)).SourceRevision);
        await remote.ActivateAsync(brain, "ping", saved.SourceRevision, TestContext.Current.CancellationToken);

        using var callerActor = VerifiedActor.Enter(actor);
        var operationId = Guid.NewGuid();
        var result = await remote.InvokeAsync(brain, "ping", "reply", "\"/ping\"",
            operationId, TestContext.Current.CancellationToken);
        Assert.Equal("completed", result.Status);
        Assert.Equal("\"surface-pong\"", result.Value);
        var retained = await remote.CancelInvocationAsync(
            brain, "ping", operationId, TestContext.Current.CancellationToken);
        Assert.Equal(operationId, retained.OperationId);
        Assert.Equal("completed", retained.Status);
    }

    private static string SourceFor(string applicationKey, string reply)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var sdkProject = Path.Combine(directory!.FullName, "src", "Kernel", "DigitalBrain.Sdk", "DigitalBrain.Sdk.csproj")
            .Replace('\\', '/');
        return $$"""
            #:project {{sdkProject}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false
            using DigitalBrain.Abstractions;
            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var application = brain.Application("{{applicationKey}}");
            application.Command<string, string>("reply", (_, _, _) => Task.FromResult("{{reply}}"));
            await application.RunAsync(args);
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
