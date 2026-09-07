using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection("Application graph builds")]
public sealed class ApplicationArtifactGraphFailureTests : IDisposable
{
    private readonly string root = Path.Combine(FindRepositoryRoot(), ".graph-failure-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 120000)]
    public async Task One_failed_worker_fails_the_graph_while_its_sibling_is_still_serving()
    {
        Directory.CreateDirectory(root);
        var ct = TestContext.Current.CancellationToken;
        var parentPath = Path.Combine(root, "start.cs");
        await File.WriteAllTextAsync(parentPath, ParentSource, ct);
        await File.WriteAllTextAsync(Path.Combine(root, "failed.cs"), ChildSource("failed"), ct);
        await File.WriteAllTextAsync(Path.Combine(root, "healthy.cs"), ChildSource("healthy"), ct);
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "graph-failure", actor);
        var host = new ApplicationArtifactHost();
        var artifact = await new FileApplicationCompiler().CompileAsync(
            parentPath, Path.Combine(root, "artifacts"), brain, "start", host, ct);
        await host.InstallAsync(artifact, brain, "start", ct);
        await File.AppendAllTextAsync(artifact.Children!["failed"].ArtifactPath, "tampered", ct);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var serving = host.ServeAsync(artifact, brain, "start", stopping.Token);
        try
        {
            var completed = await Task.WhenAny(serving, Task.Delay(TimeSpan.FromSeconds(2), ct));
            Assert.Same(serving, completed);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => serving);
            Assert.Contains("verification failed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await stopping.CancelAsync();
            try { await serving; } catch (InvalidOperationException) { }
        }
    }

    private const string ParentSource = """
        #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
        #:property TargetFramework=net11.0
        #:property PublishAot=false

        using DigitalBrain.Abstractions;
        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var app = brain.Application("start");
        _ = app.Script("failed", "failed.cs");
        _ = app.Script("healthy", "healthy.cs");
        await app.RunAsync(args);
        """;

    private static string ChildSource(string key) => $$"""
        #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
        #:property TargetFramework=net11.0
        #:property PublishAot=false

        using DigitalBrain.Abstractions;
        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var app = brain.Application("{{key}}");
        app.Command<bool, bool>("live", (_, _, _) => Task.FromResult(true));
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

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }
}
