using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationCheckpointTests
{
    [Fact(Timeout = 30000)]
    public async Task Recovered_code_cannot_skip_a_recorded_call()
    {
        var clock = new AdvanceableClock();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "determinism",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("compose");
        var child = app.Command<string, string>("greet", (name, _, _) => Task.FromResult(name));
        var reachedCheckpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var parent = app.Command<string, string>("parent", async (_, run, ct) =>
        {
            // Deliberately invalid authored code: mutable process state changes its effect sequence.
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await run.InvokeAsync(child, "Ada", ct);
                reachedCheckpoint.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            return "silently skipped the recorded call";
        });
        await app.InstallAsync("determinism-one", TestContext.Current.CancellationToken);
        var invocation = await parent.SubmitAsync("", TestContext.Current.CancellationToken);
        using var firstWorker = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "determinism-one", firstWorker.Token);
        await reachedCheckpoint.Task.WaitAsync(TestContext.Current.CancellationToken);
        await firstWorker.CancelAsync();
        await serving;
        clock.Advance(TimeSpan.FromSeconds(31));
        using var recoveredWorker = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var recovered = simulation.ServeApplicationAsync(app, "determinism-one", recoveredWorker.Token);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.ResultAsync(recoveredWorker.Token));
            Assert.Contains("Determinism violation", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await recoveredWorker.CancelAsync();
            await recovered;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Parents_waiting_for_children_do_not_exhaust_worker_slots()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "waiting-parents",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("compose");
        var child = app.Command<string, string>("child", (name, _, _) => Task.FromResult(name));
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var parent = app.Command<string, string>("parent", async (name, run, ct) =>
        {
            if (Interlocked.Increment(ref entered) == 8) { allEntered.SetResult(); }
            await allEntered.Task.WaitAsync(ct);
            return await run.InvokeAsync(child, name, ct);
        });
        await app.InstallAsync("waiting-one", TestContext.Current.CancellationToken);
        var inputs = await Task.WhenAll(Enumerable.Range(1, 8)
            .Select(i => parent.SubmitAsync(i.ToString(System.Globalization.CultureInfo.InvariantCulture), TestContext.Current.CancellationToken)));
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serving = simulation.ServeApplicationAsync(app, "waiting-one", stopping.Token);
        try
        {
            Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"],
                await Task.WhenAll(inputs.Select(input => input.ResultAsync(stopping.Token))));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Worker_recovery_reuses_the_completed_child_result()
    {
        var clock = new AdvanceableClock();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "checkpoint",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("compose");
        var calls = 0;
        var child = app.Command<string, string>("greet", (name, _, _) =>
            Task.FromResult($"Hello {name} #{Interlocked.Increment(ref calls)}"));
        var reachedCheckpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var compose = app.Command<string, string>("pair", async (_, run, ct) =>
        {
            var first = await run.InvokeAsync(child, "Ada", ct);
            reachedCheckpoint.TrySetResult();
            await resume.Task.WaitAsync(ct); // Stop the worker after the first recorded result.
            var second = await run.InvokeAsync(child, "Grace", ct);
            return $"{first}; {second}";
        });
        await app.InstallAsync("checkpoint-one", TestContext.Current.CancellationToken);
        var invocation = await compose.SubmitAsync("", TestContext.Current.CancellationToken);
        using var firstWorker = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "checkpoint-one", firstWorker.Token);
        await reachedCheckpoint.Task.WaitAsync(TestContext.Current.CancellationToken);
        await firstWorker.CancelAsync();
        await serving;

        clock.Advance(TimeSpan.FromSeconds(31));
        resume.SetResult();
        using var recoveredWorker = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var recovered = simulation.ServeApplicationAsync(app, "checkpoint-one", recoveredWorker.Token);
        try
        {
            Assert.Equal("Hello Ada #1; Hello Grace #2", await invocation.ResultAsync(recoveredWorker.Token));
            Assert.Equal(2, calls);
        }
        finally
        {
            await recoveredWorker.CancelAsync();
            await recovered;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task A_behavior_can_call_the_same_child_twice_with_distinct_inputs()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "composition",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("compose");
        var child = app.Command<string, string>("greet", (name, _, _) => Task.FromResult($"Hello {name}"));
        var compose = app.Command<string, string>("pair", async (_, run, ct) =>
        {
            var first = await run.InvokeAsync(child, "Ada", ct);
            var second = await run.InvokeAsync(child, "Grace", ct);
            return $"{first}; {second}";
        });
        await app.InstallAsync("composition-one", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "composition-one", stopping.Token);
        try
        {
            Assert.Equal("Hello Ada; Hello Grace", await compose.InvokeAsync("", stopping.Token));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    private sealed class AdvanceableClock : TimeProvider
    {
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _ticks));
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
}

