using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

[Collection("Compiled shipped startup")]
public sealed class ShippedStartupAuthoringTests
{
    [Fact(Timeout = 120000)]
    public async Task Silo_bootstrap_retains_and_supervises_the_shipped_startup_graph_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "db-shipped-authoring", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var simulation = await BrainSimulation.StartAsync(new()
            {
                // start.cs wires composer → assistant (AI) and activities → renderer (UI).
                Modules = new ModuleManifest([typeof(AIModule), typeof(UIModule)]),
                Configuration = new Dictionary<string, string?>
                {
                    [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
                },
            });
            var actor = new ActorContext(new PrincipalId(new Guid("0000dead-0000-0000-0000-000000000001")),
                "owner");
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);
            using var actorScope = VerifiedActor.Enter(actor);
            var source = Path.Combine(FindRepositoryRoot(), "src", "Kernel", "DigitalBrain.Scripting", "scripts", "start.cs");
            var firstAuthoring = new ApplicationAuthoringService(root);

            var first = await new ShippedApplicationInstaller(firstAuthoring).EnsureActiveAsync(
                brain, "start", source, cancellationToken);
            var retained = await firstAuthoring.ReadAsync(brain, "start", cancellationToken);
            Assert.Equal(first.ArtifactRevision, retained.ActiveRevision);
            Assert.Equal(["activities.cs", "start.cs", "ui.cs"],
                await firstAuthoring.ListFilesAsync(brain, "start", cancellationToken));
            var activities = await firstAuthoring.ReadFileAsync(
                brain, "start", "activities.cs", cancellationToken);
            var ui = await firstAuthoring.ReadFileAsync(brain, "start", "ui.cs", cancellationToken);
            var edited = await firstAuthoring.SaveFileAsync(brain, "start", "ui.cs",
                ui.Source.Replace("One brain", "My brain", StringComparison.Ordinal),
                ui.SourceRevision, cancellationToken);
            Assert.NotEqual(retained.SourceRevision, edited.SourceRevision);
            Assert.Equal(activities.Source, (await firstAuthoring.ReadFileAsync(
                brain, "start", "activities.cs", cancellationToken)).Source);
            var validation = await firstAuthoring.ValidateAsync(
                brain, "start", edited.SourceRevision, cancellationToken);
            Assert.True(validation.Succeeded, validation.Diagnostics);
            var ownerActivation = await firstAuthoring.ActivateAsync(
                brain, "start", edited.SourceRevision, cancellationToken);
            Assert.NotEqual(first.ArtifactRevision, ownerActivation.ArtifactRevision);

            // A new bootstrap instance must use retained metadata/artifacts instead of factory files.
            var recoveredAuthoring = new ApplicationAuthoringService(root);
            var recovered = await new ShippedApplicationInstaller(recoveredAuthoring).EnsureActiveAsync(
                brain, "start", Path.Combine(root, "factory-is-gone.cs"), cancellationToken);
            Assert.Equal(ownerActivation.ArtifactRevision, recovered.ArtifactRevision);
            Assert.Equal(edited.SourceRevision,
                (await recoveredAuthoring.ReadAsync(brain, "start", cancellationToken)).SourceRevision);

            var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
            var opened = (await renderer.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)).Delta
                .Where(entry => entry.Signal is SurfaceOpened { SurfaceKey: "home" }).ToArray();
            Assert.Equal(2, opened.Length);
            Assert.Equal("My brain", Assert.IsType<SurfaceOpened>(opened[^1].Signal).Title);
        }
        finally
        {
            await DeleteRetainedArtifactsAsync(root);
        }
    }

    private static async Task DeleteRetainedArtifactsAsync(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); }
                return;
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && attempt < 9)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(50 * (attempt + 1));
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
