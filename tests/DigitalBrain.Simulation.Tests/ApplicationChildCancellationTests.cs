using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationChildCancellationTests : IDisposable
{
    private readonly string persistenceDirectory = Path.Combine(
        Path.GetTempPath(), "digitalbrain-child-cancellation-tests", Guid.NewGuid().ToString("N"));

    [Theory(Timeout = 30000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_a_parent_cooperatively_stops_its_running_child(bool parallel)
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "child-cancel-running", actor);
        using var verified = VerifiedActor.Enter(actor);
        var childStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = brain.Application("child");
        var childCommand = child.Command<string, string>("work", async (_, _, ct) =>
        {
            childStarted.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { childStopped.TrySetResult(); }
            return "unreachable";
        });
        await child.InstallAsync("child-v1", TestContext.Current.CancellationToken);
        var parent = brain.Application("parent");
        var parentCommand = parent.Command<string, string>("run",
            (_, run, ct) => parallel
                ? run.Branch("child").InvokeAsync(childCommand, "work", ct)
                : run.InvokeAsync(childCommand, "work", ct));
        await parent.InstallAsync("parent-v1", TestContext.Current.CancellationToken);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var childServing = simulation.ServeApplicationAsync(child, "child-v1", stopping.Token);
        var parentServing = simulation.ServeApplicationAsync(parent, "parent-v1", stopping.Token);
        try
        {
            var invocation = await parentCommand.SubmitAsync("start", TestContext.Current.CancellationToken);
            await childStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await invocation.CancelAsync(TestContext.Current.CancellationToken);
            await childStopped.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => invocation.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await SuppressCancellation(childServing, parentServing);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task An_offline_child_of_a_cancelled_parent_does_not_run_after_restart()
    {
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = persistenceDirectory,
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "child-cancel-recovery", actor);
        using var verified = VerifiedActor.Enter(actor);
        var executed = 0;
        var child = brain.Application("child");
        var childCommand = child.Command<string, string>("work", (_, _, _) =>
        {
            Interlocked.Increment(ref executed);
            return Task.FromResult("ran");
        });
        await child.InstallAsync("child-v1", TestContext.Current.CancellationToken);
        var parent = brain.Application("parent");
        var parentCommand = parent.Command<string, string>("run",
            (_, run, ct) => run.InvokeAsync(childCommand, "work", ct));
        await parent.InstallAsync("parent-v1", TestContext.Current.CancellationToken);
        var invocation = await parentCommand.SubmitAsync("start", TestContext.Current.CancellationToken);

        using (var parentStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var parentServing = simulation.ServeApplicationAsync(parent, "parent-v1", parentStopping.Token);
            await WaitUntilAsync(async () =>
                (await child.PendingRevisionsAsync(TestContext.Current.CancellationToken)).Contains("child-v1"));
            await invocation.CancelAsync(TestContext.Current.CancellationToken);
            await parentStopping.CancelAsync();
            await SuppressCancellation(parentServing);
        }

        await simulation.RestartSiloAsync(TestContext.Current.CancellationToken);
        await using var recovered = DigitalBrainClient.Connect(simulation.Grains, "child-cancel-recovery", actor);
        var recoveredChild = recovered.Application("child");
        recoveredChild.Command<string, string>("work", (_, _, _) =>
        {
            Interlocked.Increment(ref executed);
            return Task.FromResult("ran");
        });
        using var childStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var childServingAfterRestart = simulation.ServeApplicationAsync(
            recoveredChild, "child-v1", childStopping.Token);
        try
        {
            await WaitUntilAsync(async () =>
                !(await recoveredChild.PendingRevisionsAsync(TestContext.Current.CancellationToken)).Contains("child-v1"));
            Assert.Equal(0, Volatile.Read(ref executed));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => invocation.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await childStopping.CancelAsync();
            await SuppressCancellation(childServingAfterRestart);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        while (!await condition())
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private static async Task SuppressCancellation(params Task[] tasks)
    {
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (Directory.Exists(persistenceDirectory))
        {
            Directory.Delete(persistenceDirectory, recursive: true);
        }
    }
}
