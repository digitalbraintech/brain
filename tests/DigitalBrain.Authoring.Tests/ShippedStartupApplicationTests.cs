using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.AI;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

[Collection("Compiled shipped startup")]
public sealed class ShippedStartupApplicationTests : IDisposable
{
    private readonly string artifacts = Path.Combine(
        Path.GetTempPath(), "db-shipped-startup", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 120000)]
    public async Task Shipped_startup_graph_initializes_the_brain_and_opens_home_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule), typeof(UIModule)]),
            Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
        });
        var actor = new ActorContext(PrincipalId.New(), "startup-owner");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "shipped-startup", actor);
        var host = new ApplicationArtifactHost();
        var source = Path.Combine(FindRepositoryRoot(), "src", "Kernel", "DigitalBrain.Scripting", "scripts", "start.cs");
        var artifact = await new FileApplicationCompiler().CompileAsync(
            source, artifacts, brain, "start", host, cancellationToken);

        await host.InstallAsync(artifact, brain, "start", cancellationToken);
        await host.InstallAsync(artifact, brain, "start", cancellationToken);

        Assert.Single((await brain.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)).Delta,
            entry => entry.Signal is DigitalBrainActivated);
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        Assert.Single((await renderer.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)).Delta,
            entry => entry.Signal is SurfaceOpened { SurfaceKey: "home" });
        var surface = await brain.GetEntity<ISurface>(ISurface.DefaultInstanceName).Read();
        Assert.NotNull(surface);
        Assert.Contains(surface.Scenes, scene => scene.SurfaceKey == "home");
    }

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(artifacts)) { Directory.Delete(artifacts, recursive: true); }
                return;
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && attempt < 9)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
