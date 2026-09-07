using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationDefinitionBoundaryTests
{
    [Theory(Timeout = 120000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_requires_exactly_one_RunAsync_boundary(bool twice)
    {
        var root = Path.Combine(FindRepositoryRoot(), ".definition-boundary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "boundary.cs");
            await File.WriteAllTextAsync(source, Source(twice), TestContext.Current.CancellationToken);
            var artifact = await new FileApplicationCompiler().CompileAsync(
                source, Path.Combine(root, "artifacts"), TestContext.Current.CancellationToken);
            await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "definition-boundary",
                new ActorContext(PrincipalId.New(), "author"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ApplicationArtifactHost().ValidateAsync(
                    artifact, brain, "boundary", TestContext.Current.CancellationToken));

            Assert.Contains("RunAsync", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Source(bool twice) => $$"""
        #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
        #:property TargetFramework=net11.0
        #:property PublishAot=false

        using DigitalBrain.Abstractions;
        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var app = brain.Application("boundary");
        app.Command<string, string>("echo", (text, _, _) => Task.FromResult(text));
        {{(twice ? "await app.RunAsync(args);\nawait app.RunAsync(args);" : "// RunAsync is intentionally missing.")}}
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
