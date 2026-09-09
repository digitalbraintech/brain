using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using Xunit;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationProcessWorkerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "db-process-worker", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 90000)]
    public async Task Killed_artifact_process_restarts_and_completes_its_original_durable_command()
    {
        Directory.CreateDirectory(root);
        var clock = new AdvanceableClock(DateTimeOffset.UtcNow);
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            UseExternalGateway = true,
            PersistenceDirectory = Path.Combine(root, "persistence"),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        await using var bootstrap = await ApplicationWorkerBootstrapServer.StartAsync(
            simulation.ExternalGateway,
            simulation.GetSiloService<ApplicationWorkerCapabilityAuthority>(),
            TestContext.Current.CancellationToken);
        var host = new ApplicationArtifactHost(bootstrap);
        var actor = new ActorContext(PrincipalId.New(), "process-worker-test");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "process-owner", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        var pidPath = Path.Combine(root, "worker.pid");
        var gatePath = Path.Combine(root, "release.gate");
        var sourcePath = Path.Combine(root, "worker.cs");
        await File.WriteAllTextAsync(sourcePath, SourceFor(pidPath, gatePath), TestContext.Current.CancellationToken);
        var artifact = await new FileApplicationCompiler().CompileAsync(
            sourcePath, Path.Combine(root, "artifacts"), TestContext.Current.CancellationToken);
        await host.InstallAsync(artifact, brain, "process-app", TestContext.Current.CancellationToken);

        var command = brain.Application("process-app").Command<string, string>(
            "reply", (_, _, _) => Task.FromResult("caller-handler-must-not-run"));
        var accepted = await command.SubmitAsync(
            "/ping", Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var firstStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var firstServing = host.ServeAsync(artifact, brain, "process-app", firstStopping.Token);
        var firstPidRead = ReadChangedPidAsync(pidPath, previous: null, TestContext.Current.CancellationToken);
        if (await Task.WhenAny(firstPidRead, firstServing) == firstServing) { await firstServing; }
        var firstPid = await firstPidRead;
        Assert.NotEqual(Environment.ProcessId, firstPid);

        await firstStopping.CancelAsync();
        await firstServing;
        Assert.True(ProcessHasExited(firstPid), "Cancellation must kill the complete artifact process tree.");

        clock.Advance(TimeSpan.FromMinutes(1));
        using var secondStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var secondServing = host.ServeAsync(artifact, brain, "process-app", secondStopping.Token);
        try
        {
            var secondPid = await ReadChangedPidAsync(pidPath, firstPid, TestContext.Current.CancellationToken);
            Assert.NotEqual(firstPid, secondPid);
            await File.WriteAllTextAsync(gatePath, "release", TestContext.Current.CancellationToken);
            Assert.Equal("worker-pong", await accepted.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await secondStopping.CancelAsync();
            await secondServing;
        }
    }

    private static async Task<int> ReadChangedPidAsync(
        string path, int? previous, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path) && int.TryParse(
                    await File.ReadAllTextAsync(path, cancellationToken), out var pid) && pid != previous)
            {
                return pid;
            }
            await Task.Delay(25, cancellationToken);
        }
    }

    private static bool ProcessHasExited(int pid)
    {
        try { return Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return true; }
    }

    private static string SourceFor(string pidPath, string gatePath)
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
            // Aspire's silo provider settings must not configure this client-only worker.
            if (args.Contains("--digitalbrain-worker-endpoint"))
            {
                Environment.SetEnvironmentVariable("Orleans__Reminders__ProviderType", "AzureTableStorage");
            }
            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var application = brain.Application("process-app");
            application.Command<string, string>("reply", async (_, _, ct) =>
            {
                await File.WriteAllTextAsync({{JsonSerializer.Serialize(pidPath)}}, Environment.ProcessId.ToString(), ct);
                while (!File.Exists({{JsonSerializer.Serialize(gatePath)}}))
                {
                    await Task.Delay(25, ct);
                }
                return "worker-pong";
            });
            await application.RunAsync(args);
            """;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AdvanceableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }
}

