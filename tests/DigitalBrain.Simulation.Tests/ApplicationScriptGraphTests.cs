using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection("Application graph builds")]
public sealed class ApplicationScriptGraphTests
{
    [Fact(Timeout = 120000)]
    public async Task Parent_artifact_pins_child_source_and_reapplies_only_when_effective_graph_changes()
    {
        var root = Path.Combine(FindRepositoryRoot(), ".script-graph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var child = Path.Combine(root, "child.cs");
            var unused = Path.Combine(root, "unused.cs");
            var parent = Path.Combine(root, "start.cs");
            await File.WriteAllTextAsync(child, ChildSource(increment: 1), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(unused, UnusedSource, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(parent, ParentSource, TestContext.Current.CancellationToken);
            await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "script-graph",
                new ActorContext(PrincipalId.New(), "author"));
            var host = new ApplicationArtifactHost();
            var compiler = new FileApplicationCompiler();
            var oldArtifact = await compiler.CompileAsync(parent, Path.Combine(root, "artifacts"), brain, "start",
                host, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(child, ChildSource(increment: 10), TestContext.Current.CancellationToken);
            var changed = await compiler.CompileAsync(parent, Path.Combine(root, "artifacts"), brain, "start",
                host, TestContext.Current.CancellationToken);
            Assert.NotEqual(oldArtifact.ApplicationRevision, changed.ApplicationRevision);

            await host.InstallAsync(oldArtifact, brain, "start", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                brain.Application("unused").Command<bool, bool>("never").HeadAsync());
            await host.InstallAsync(oldArtifact, brain, "start", TestContext.Current.CancellationToken);

            var childApp = brain.Application("child");
            var settings = childApp.Behavior("settings");
            var count = settings.State<int>("count", schemaVersion: 1);
            var increment = settings.Command<int, int>("increment", (amount, run, _) =>
            {
                run.State.Set(count, run.State.Get(count) + amount);
                return Task.FromResult(run.State.Get(count));
            });
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var serving = host.ServeAsync(oldArtifact, brain, "start", stopping.Token);
            try
            {
                Assert.Equal(2, await increment.InvokeAsync(1, TestContext.Current.CancellationToken));
            }
            finally
            {
                await stopping.CancelAsync();
                await serving;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ChildSource(int increment) => $$"""
        #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
        #:property TargetFramework=net11.0
        #:property PublishAot=false

        using DigitalBrain.Abstractions;
        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var app = brain.Application("child");
        var settings = app.Behavior("settings");
        var count = settings.State<int>("count", schemaVersion: 1);
        var increment = settings.Command<int, int>("increment", (amount, run, _) =>
        {
            run.State.Set(count, run.State.Get(count) + amount);
            return Task.FromResult(run.State.Get(count));
        });
        app.OnApply(async (run, ct) => { await run.InvokeAsync(increment, {{increment}}, ct); });
        await app.RunAsync(args);
        """;

    private const string ParentSource = """
        #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
        #:property TargetFramework=net11.0
        #:property PublishAot=false

        using DigitalBrain.Abstractions;
        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var app = brain.Application("start");
        var child = app.Script("child", "child.cs");
        _ = app.Script("unused", "unused.cs");
        app.OnApply(async (run, ct) => { await run.ApplyScriptAsync(child, ct); });
        await app.RunAsync(args);
        """;

    private const string UnusedSource = """
        #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
        #:property TargetFramework=net11.0
        #:property PublishAot=false

        using DigitalBrain.Abstractions;
        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var app = brain.Application("unused");
        app.Command<bool, bool>("never", (_, _, _) =>
            throw new InvalidOperationException("Declared-only child was executed."));
        app.OnApply((_, _) => throw new InvalidOperationException("Declared-only child was applied."));
        await app.RunAsync(args);
        """;

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

