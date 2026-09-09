using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationScriptBundleBoundaryTests
{
    [Theory(Timeout = 90000)]
    [InlineData("../foreign.cs")]
    [InlineData("/foreign.cs")]
    public async Task Definition_rejects_a_child_script_path_outside_the_root_source_bundle(string childPath)
    {
        var root = Path.Combine(FindRepositoryRoot(), ".script-graph-tests", Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(root, "bundle");
        Directory.CreateDirectory(bundle);
        try
        {
            var parent = Path.Combine(bundle, "start.cs");
            await File.WriteAllTextAsync(parent, Source("start", childPath), TestContext.Current.CancellationToken);
            await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "script-boundary",
                new ActorContext(PrincipalId.New(), "author"));

            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                new FileApplicationCompiler().CompileAsync(parent, Path.Combine(root, "artifacts"), brain, "start",
                    new ApplicationArtifactHost(), TestContext.Current.CancellationToken));

            Assert.Contains("relative", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Source(string key, string? childPath)
    {
        var sdk = Path.Combine(FindRepositoryRoot(), "src", "Kernel", "DigitalBrain.Sdk", "DigitalBrain.Sdk.csproj")
            .Replace('\\', '/');
        var declaration = childPath is null
            ? string.Empty
            : $"_ = app.Script(\"foreign\", @\"{childPath.Replace("\"", "\"\"")}\");";
        return $$"""
            #:project {{sdk}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;
            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var app = brain.Application("{{key}}");
            {{declaration}}
            await app.RunAsync(args);
            """;
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
