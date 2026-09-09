using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationProgramEventWaitTests : IDisposable
{
    private readonly string persistence = Path.Combine(
        Path.GetTempPath(), "db-program-event-wait", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Authored_event_wait_recovers_event_published_while_waiter_worker_is_stopped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = persistence,
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "program-events", actor);
        var publisher = brain.Application("publisher");
        var source = publisher.Behavior("source");
        var changed = source.Event<string>("changed");
        var publish = source.Command<string, bool>("publish", async (value, run, ct) =>
        {
            await run.PublishAsync(changed, value, ct);
            return true;
        });
        await publisher.InstallAsync("publisher-r1", cancellationToken);
        var waiter = brain.Application("waiter");
        var wait = waiter.Command<string, string>("wait", (_, run, ct) => run.WaitForAsync(changed, ct));
        await waiter.InstallAsync("waiter-r1", cancellationToken);
        var invocation = await wait.SubmitAsync("next", Guid.NewGuid(), cancellationToken);
        using (var waiterStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var waiterWorker = simulation.ServeApplicationAsync(waiter, "waiter-r1", waiterStopping.Token);
            await WaitUntilAsync(async () =>
                (await waiter.ReadInvocationAsync(invocation.OperationId, cancellationToken)).Status == "waiting",
                cancellationToken);
            await waiterStopping.CancelAsync();
            await waiterWorker;
        }
        using (var publisherStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var publisherWorker = simulation.ServeApplicationAsync(publisher, "publisher-r1", publisherStopping.Token);
            Assert.True(await publish.InvokeAsync("retained-value", cancellationToken));
            await publisherStopping.CancelAsync();
            await publisherWorker;
        }

        await simulation.RestartSiloAsync(cancellationToken);
        await using var recoveredBrain = DigitalBrainClient.Connect(simulation.Grains, "program-events", actor);
        var recoveredPublisher = recoveredBrain.Application("publisher");
        var recoveredChanged = recoveredPublisher.Behavior("source").Event<string>("changed");
        var recoveredWaiter = recoveredBrain.Application("waiter");
        var recoveredWait = recoveredWaiter.Command<string, string>(
            "wait", (_, run, ct) => run.WaitForAsync(recoveredChanged, ct));
        var recoveredInvocation = await recoveredWait.SubmitAsync(
            "next", invocation.OperationId, cancellationToken);
        using var recoveredStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var recoveredWorker = simulation.ServeApplicationAsync(
            recoveredWaiter, "waiter-r1", recoveredStopping.Token);
        try
        {
            Assert.Equal("retained-value", await recoveredInvocation.ResultAsync(cancellationToken));
        }
        finally
        {
            await recoveredStopping.CancelAsync();
            await recoveredWorker;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(persistence)) { Directory.Delete(persistence, recursive: true); }
    }
}
