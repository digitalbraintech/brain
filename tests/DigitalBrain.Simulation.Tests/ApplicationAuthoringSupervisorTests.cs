using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationAuthoringSupervisorTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(Path.GetTempPath(), "db-supervisor", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Activated_source_is_served_by_the_silo_and_recovers_after_restart()
    {
        var brokenDirectory = Path.Combine(storeRoot, "broken");
        Directory.CreateDirectory(brokenDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(brokenDirectory, "metadata.json"), "{not-json",
            TestContext.Current.CancellationToken);
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            UseExternalGateway = true,
            PersistenceDirectory = Path.Combine(storeRoot, "grain-storage"),
            ConfigureSilo = silo => silo.Services.AddApplicationAuthoring(storeRoot),
        });
        var authoring = simulation.GetSiloService<IApplicationAuthoring>();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "supervised", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        var saved = await authoring.SaveAsync(
            brain, "greeting", SourceFor("greeting", "supervised-pong"), null,
            TestContext.Current.CancellationToken);
        var validation = await authoring.ValidateAsync(
            brain, "greeting", saved.SourceRevision, TestContext.Current.CancellationToken);
        Assert.True(validation.Succeeded, validation.Diagnostics);
        await authoring.ActivateAsync(
            brain, "greeting", saved.SourceRevision, TestContext.Current.CancellationToken);

        Assert.Equal("supervised-pong", await InvokeAsync(brain, TestContext.Current.CancellationToken));
        await simulation.RestartSiloAsync(TestContext.Current.CancellationToken);
        await using var recovered = DigitalBrainClient.Connect(simulation.Grains, "supervised", actor);
        Assert.Equal("supervised-pong", await InvokeAsync(recovered, TestContext.Current.CancellationToken));
    }

    private static async Task<string> InvokeAsync(IDigitalBrain brain, CancellationToken cancellationToken)
    {
        var command = brain.Application("greeting").Command<string, string>(
            "reply", (_, _, _) => Task.FromResult("caller-handler-must-not-run"));
        return await command.InvokeAsync("/ping", cancellationToken);
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

