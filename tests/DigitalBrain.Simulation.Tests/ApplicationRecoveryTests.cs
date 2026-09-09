using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationRecoveryTests : IDisposable
{
    private readonly string persistenceDirectory = Path.Combine(
        Path.GetTempPath(), "digitalbrain-recovery-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Accepted_command_survives_silo_restart_on_its_installed_revision()
    {
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = persistenceDirectory,
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var client = DigitalBrainClient.Connect(simulation.Grains, "recovery", actor);
        var oldApplication = client.Application("greeting");
        var oldCommand = oldApplication.Command<string, string>("reply", (_, _, _) => Task.FromResult("old-pong"));
        await oldApplication.InstallAsync("old-revision", TestContext.Current.CancellationToken);
        var acceptedBeforeRestart = await oldCommand.SubmitAsync(
            "/ping", Guid.NewGuid(), TestContext.Current.CancellationToken);

        var newApplication = client.Application("greeting");
        var newCommand = newApplication.Command<string, string>("reply", (_, _, _) => Task.FromResult("new-pong"));
        await newApplication.InstallAsync("new-revision", TestContext.Current.CancellationToken);
        await simulation.RestartSiloAsync(TestContext.Current.CancellationToken);

        await using var recoveredClient = DigitalBrainClient.Connect(simulation.Grains, "recovery", actor);
        var recoveredOld = recoveredClient.Application("greeting");
        recoveredOld.Command<string, string>("reply", (_, _, _) => Task.FromResult("old-pong"));
        using var oldStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var oldWorker = simulation.ServeApplicationAsync(recoveredOld, "old-revision", oldStopping.Token);
        try
        {
            Assert.Equal("old-pong", await acceptedBeforeRestart.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await oldStopping.CancelAsync();
            await oldWorker;
        }

        var acceptedAfterRestart = await newCommand.SubmitAsync(
            "/ping", Guid.NewGuid(), TestContext.Current.CancellationToken);
        var recoveredNew = recoveredClient.Application("greeting");
        recoveredNew.Command<string, string>("reply", (_, _, _) => Task.FromResult("new-pong"));
        using var newStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var newWorker = simulation.ServeApplicationAsync(recoveredNew, "new-revision", newStopping.Token);
        try
        {
            Assert.Equal("new-pong", await acceptedAfterRestart.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await newStopping.CancelAsync();
            await newWorker;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(persistenceDirectory))
        {
            Directory.Delete(persistenceDirectory, recursive: true);
        }
    }
}

