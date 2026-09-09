using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationCancellationTests : IDisposable
{
    private readonly string persistenceDirectory = Path.Combine(
        Path.GetTempPath(), "digitalbrain-cancellation-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Cancelling_pending_work_is_retained_across_a_silo_restart()
    {
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = persistenceDirectory,
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "cancel-pending", actor);
        var app = brain.Application("jobs");
        var command = app.Command<string, string>("run", (value, _, _) => Task.FromResult(value));
        await app.InstallAsync("jobs-v1", TestContext.Current.CancellationToken);
        var invocation = await command.SubmitAsync("never run", TestContext.Current.CancellationToken);

        await invocation.CancelAsync(TestContext.Current.CancellationToken);
        await simulation.RestartSiloAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invocation.ResultAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 30000)]
    public async Task Cancellation_stops_a_running_handler_but_does_not_replace_a_completed_result()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "cancel-running", actor);
        var app = brain.Application("jobs");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = app.Command<string, string>("slow", async (_, _, ct) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { stopped.TrySetResult(); }
            return "unreachable";
        });
        var fast = app.Command<string, string>("fast", (value, _, _) => Task.FromResult(value));
        await app.InstallAsync("jobs-v1", TestContext.Current.CancellationToken);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "jobs-v1", stopping.Token);
        try
        {
            var running = await slow.SubmitAsync("work", TestContext.Current.CancellationToken);
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await running.CancelAsync(TestContext.Current.CancellationToken);
            await stopped.Task.WaitAsync(TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => running.ResultAsync(TestContext.Current.CancellationToken));

            var completed = await fast.SubmitAsync("kept", TestContext.Current.CancellationToken);
            Assert.Equal("kept", await completed.ResultAsync(TestContext.Current.CancellationToken));
            await completed.CancelAsync(TestContext.Current.CancellationToken);
            Assert.Equal("kept", await completed.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
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
