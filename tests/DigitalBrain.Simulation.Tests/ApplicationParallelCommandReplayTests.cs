using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationParallelCommandReplayTests
{
    [Fact(Timeout = 30000)]
    public async Task Recorded_branch_result_remains_pinned_when_the_child_head_advances()
    {
        var clock = new AdvanceableClock();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "branch-replay", actor);
        using var verified = VerifiedActor.Enter(actor);

        var childV1 = brain.Application("child");
        var childV1Calls = 0;
        childV1.Command<string, string>("value", (_, _, _) =>
        {
            Interlocked.Increment(ref childV1Calls);
            return Task.FromResult("v1");
        });
        await childV1.InstallAsync("child-v1", TestContext.Current.CancellationToken);

        var effectRecorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdParent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parentV1 = brain.Application("parent");
        var parentCommand = DeclareParent(parentV1, brain, effectRecorded, holdParent);
        await parentV1.InstallAsync("parent-v1", TestContext.Current.CancellationToken);
        var invocation = await parentCommand.SubmitAsync("request", TestContext.Current.CancellationToken);

        using (var firstStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var childServing = simulation.ServeApplicationAsync(childV1, "child-v1", firstStopping.Token);
            var parentServing = simulation.ServeApplicationAsync(parentV1, "parent-v1", firstStopping.Token);
            await effectRecorded.Task.WaitAsync(TestContext.Current.CancellationToken);
            await firstStopping.CancelAsync();
            await SuppressCancellation(childServing, parentServing);
        }
        Assert.Equal(1, childV1Calls);

        clock.Advance(TimeSpan.FromSeconds(31));
        var childV2 = brain.Application("child");
        var childV2Calls = 0;
        childV2.Command<string, string>("value", (_, _, _) =>
        {
            Interlocked.Increment(ref childV2Calls);
            return Task.FromResult("v2");
        });
        await childV2.InstallAsync("child-v2", TestContext.Current.CancellationToken);

        var resumedParent = brain.Application("parent");
        DeclareParent(resumedParent, brain);
        using var resumedStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var childV2Serving = simulation.ServeApplicationAsync(childV2, "child-v2", resumedStopping.Token);
        var parentV1Serving = simulation.ServeApplicationAsync(resumedParent, "parent-v1", resumedStopping.Token);
        try
        {
            Assert.Equal("v1", await invocation.ResultAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, childV2Calls);
        }
        finally
        {
            await resumedStopping.CancelAsync();
            await SuppressCancellation(childV2Serving, parentV1Serving);
        }
    }

    private static async Task SuppressCancellation(params Task[] tasks)
    {
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    private static CommandPort<string, string> DeclareParent(
        ApplicationDefinition parent,
        DigitalBrainClient brain,
        TaskCompletionSource? effectRecorded = null,
        TaskCompletionSource? hold = null)
        => parent.Command<string, string>("compose", async (_, run, ct) =>
        {
            var child = brain.Application("child").Command<string, string>("value");
            var value = await run.Branch("child").InvokeAsync(child, "request", ct);
            effectRecorded?.TrySetResult();
            if (hold is not null) { await hold.Task.WaitAsync(ct); }
            return value;
        });

    private sealed class AdvanceableClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }
}
