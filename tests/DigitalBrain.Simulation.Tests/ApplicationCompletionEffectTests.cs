using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationCompletionEffectTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "db-application-completion", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Reapplying_completed_configuration_after_restart_does_not_repeat_neuron_command()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            PersistenceDirectory = Path.Combine(root, "persistence"),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "completion-owner", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "surface.cs");
        await File.WriteAllTextAsync(sourcePath, Source(), cancellationToken);
        var artifact = await new FileApplicationCompiler().CompileAsync(
            sourcePath, Path.Combine(root, "artifacts"), cancellationToken);
        var host = new ApplicationArtifactHost();

        await host.InstallAsync(artifact, brain, "surface-startup", cancellationToken);
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        Assert.Single((await renderer.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)).Delta,
            delivery => delivery.Signal is SurfaceOpened { SurfaceKey: "home", Title: "Home" });
        var surface = await brain.GetEntity<ISurface>(ISurface.DefaultInstanceName).Read();
        Assert.NotNull(surface);
        Assert.Contains(surface.Scenes, scene => scene.SurfaceKey == "home" && scene.Title == "Home");

        await simulation.RestartSiloAsync(cancellationToken);
        await using var recovered = DigitalBrainClient.Connect(simulation.Grains, "completion-owner", actor);
        using var recoveredScope = VerifiedActor.Enter(actor);
        await host.InstallAsync(artifact, recovered, "surface-startup", cancellationToken);
        var recoveredRenderer = recovered.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        Assert.Single((await recoveredRenderer.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)).Delta,
            delivery => delivery.Signal is SurfaceOpened { SurfaceKey: "home", Title: "Home" });
    }

    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var sdk = Path.Combine(directory!.FullName, "src", "Kernel", "DigitalBrain.Sdk", "DigitalBrain.Sdk.csproj")
            .Replace('\\', '/');
        var ui = Path.Combine(directory.FullName, "src", "Modules", "UI",
                "DigitalBrain.Modules.UI.Contracts", "DigitalBrain.Modules.UI.Contracts.csproj")
            .Replace('\\', '/');
        var commandId = Guid.NewGuid();
        return $$"""
            #:project {{sdk}}
            #:project {{ui}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false
            using DigitalBrain.Abstractions;
            using DigitalBrain.Product.Identity;
            using DigitalBrain.UI;
            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var application = brain.Application("surface-startup");
            var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
            application.OnApply(async (run, ct) =>
                await run.SendAsync(renderer,
                    new OpenSurface(new CommandId(new Guid({{JsonSerializer.Serialize(commandId.ToString())}})), "home", "Home"), ct));
            await application.RunAsync(args);
            """;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }
}
