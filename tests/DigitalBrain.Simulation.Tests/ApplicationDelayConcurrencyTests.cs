using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationDelayConcurrencyTests
{
    [Fact(Timeout = 30000)]
    public async Task Waiting_delay_releases_worker_and_does_not_block_an_unrelated_state_scope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "delay-concurrency",
            new ActorContext(PrincipalId.New(), "author"));
        var application = brain.Application("delays");
        var delayedBehavior = application.Behavior("delayed");
        delayedBehavior.State<int>("count", 1);
        var delayed = delayedBehavior.Command<string, string>("wait", async (_, run, ct) =>
        {
            await run.DelayAsync(TimeSpan.FromHours(1), ct);
            return "late";
        });
        var readyBehavior = application.Behavior("ready");
        readyBehavior.State<int>("count", 1);
        var ready = readyBehavior.Command<string, string>("reply", (_, _, _) => Task.FromResult("ready"));
        await application.InstallAsync("r1", cancellationToken);
        var waiting = await delayed.SubmitAsync("start", Guid.NewGuid(), cancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(application, "r1", stopping.Token);
        try
        {
            await WaitUntilAsync(async () =>
                (await application.ReadInvocationAsync(waiting.OperationId, cancellationToken)).Status == "waiting",
                cancellationToken);
            Assert.Equal("ready", await ready.InvokeAsync("now", cancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }
}
