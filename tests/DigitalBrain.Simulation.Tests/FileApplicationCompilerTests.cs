using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class FileApplicationCompilerTests : IDisposable
{
    private readonly string testRoot;

    public FileApplicationCompilerTests()
    {
        var repositoryRoot = FindRepositoryRoot();
        testRoot = Path.Combine(repositoryRoot, ".file-compiler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
    }

    [Fact]
    public async Task CompileAsync_publishes_an_immutable_executable_source_revision()
    {
        var sourcePath = Path.Combine(testRoot, "hello.cs");
        var outputRoot = Path.Combine(testRoot, "artifacts");
        const string originalSource = """
            #:project ../../src/Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions.Identity;

            Console.Write($"first:{nameof(NeuronId)}");
            """;
        await File.WriteAllTextAsync(sourcePath, originalSource, TestContext.Current.CancellationToken);

        var artifact = await new FileApplicationCompiler().CompileAsync(sourcePath, outputRoot, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(sourcePath, "Console.Write(\"second\");", TestContext.Current.CancellationToken);
        var output = await RunAsync(artifact.ArtifactPath, TestContext.Current.CancellationToken);

        Assert.Equal("first:NeuronId", output);
        Assert.Equal(originalSource, await File.ReadAllTextAsync(artifact.SourcePath, TestContext.Current.CancellationToken));
        Assert.NotEqual(artifact.SourceHash, artifact.RevisionId);
        Assert.StartsWith(Path.GetFullPath(outputRoot), artifact.ArtifactPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompileAsync_rejects_a_source_changed_during_publish()
    {
        var sourcePath = Path.Combine(testRoot, "changing.cs");
        var outputRoot = Path.Combine(testRoot, "artifacts");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            #:project ../../src/Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            Console.Write("original");
            """,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(testRoot, "Directory.Build.targets"),
            """
            <Project>
              <Target Name="MutateSource" BeforeTargets="CoreCompile">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)\changing.cs" Lines="// changed during build" Overwrite="false" />
              </Target>
            </Project>
            """,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FileApplicationCompiler().CompileAsync(sourcePath, outputRoot, TestContext.Current.CancellationToken));

        Assert.Contains("changed during publish", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outputRoot));
    }

    [Fact(Timeout = 60000)]
    public async Task Artifact_host_installs_and_serves_only_the_compiled_revision()
    {
        var sourcePath = Path.Combine(testRoot, "artifact-app.cs");
        var outputRoot = Path.Combine(testRoot, "artifacts");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var application = brain.Application("artifact-greeting");
            application.Command<string, string>("reply", (message, _, _) =>
                Task.FromResult(message == "/ping" ? "original-pong" : "ignored"));
            await application.RunAsync(args);
            """,
            TestContext.Current.CancellationToken);
        var artifact = await new FileApplicationCompiler().CompileAsync(
            sourcePath, outputRoot, TestContext.Current.CancellationToken);
        var host = new ApplicationArtifactHost();
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");

        await using (var installer = DigitalBrainClient.Connect(simulation.Grains, "artifact-owner", actor))
        {
            await host.InstallAsync(artifact, installer, "artifact-greeting", TestContext.Current.CancellationToken);
        }

        await File.WriteAllTextAsync(sourcePath, "Console.Write(\"replacement\");", TestContext.Current.CancellationToken);
        await using var caller = DigitalBrainClient.Connect(simulation.Grains, "artifact-owner", actor);
        var command = caller.Application("artifact-greeting").Command<string, string>(
            "reply", (_, _, _) => Task.FromResult("caller-handler-must-not-run"));
        var invocation = await command.SubmitAsync("/ping", TestContext.Current.CancellationToken);

        await using var worker = DigitalBrainClient.Connect(simulation.Grains, "artifact-owner", actor);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = host.ServeAsync(artifact, worker, "artifact-greeting", stopping.Token);
        try
        {
            Assert.Equal("original-pong", await invocation.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact]
    public async Task Artifact_host_rejects_a_tampered_artifact()
    {
        var sourcePath = Path.Combine(testRoot, "tamper.cs");
        var outputRoot = Path.Combine(testRoot, "artifacts");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            Console.Write("original");
            """,
            TestContext.Current.CancellationToken);
        var artifact = await new FileApplicationCompiler().CompileAsync(
            sourcePath, outputRoot, TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(artifact.ArtifactPath, "tampered", TestContext.Current.CancellationToken);
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "tamper", new ActorContext(PrincipalId.New(), "author"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ApplicationArtifactHost().InstallAsync(artifact, brain, "tamper", TestContext.Current.CancellationToken));

        Assert.Contains("verification", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bound_execution_connection_is_borrowed_and_cannot_dispose_the_host_brain()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var hostBrain = DigitalBrainClient.Connect(
            simulation.Grains, "borrowed", new ActorContext(PrincipalId.New(), "author"));
        IDigitalBrain borrowed;
        using (DigitalBrainClient.BindExecution(hostBrain))
        {
            borrowed = await DigitalBrainClient.ConnectAsync([], TestContext.Current.CancellationToken);
        }

        Assert.NotSame(hostBrain, borrowed);
        await borrowed.DisposeAsync();
        Assert.Equal(hostBrain.Owner, borrowed.Owner);
    }

    [Fact]
    public async Task Artifact_install_rejects_top_level_business_effects_before_definition_install()
    {
        var sourcePath = Path.Combine(testRoot, "effect.cs");
        var outputRoot = Path.Combine(testRoot, "artifacts");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            await brain.ActivateAsync();
            var application = brain.Application("effect");
            application.Command<string, string>("reply", (_, _, _) => Task.FromResult("pong"));
            await application.RunAsync(args);
            """,
            TestContext.Current.CancellationToken);
        var artifact = await new FileApplicationCompiler().CompileAsync(
            sourcePath, outputRoot, TestContext.Current.CancellationToken);
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "effect-owner", actor);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ApplicationArtifactHost().InstallAsync(
                artifact, brain, "effect", TestContext.Current.CancellationToken));

        Assert.Contains("definition", error.Message, StringComparison.OrdinalIgnoreCase);
        var outgoing = await brain.ReadJournalAsync(
            JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Signal is DigitalBrainActivated);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task<string> RunAsync(string artifactPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(artifactPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the published application.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.True(process.ExitCode == 0, await standardError);
        return await standardOutput;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

