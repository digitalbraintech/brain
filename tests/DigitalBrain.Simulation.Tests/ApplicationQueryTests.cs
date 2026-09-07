using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationQueryTests
{
    [Fact(Timeout = 30000)]
    public async Task Query_cannot_invoke_a_captured_command_port()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "query-effects",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("counter");
        var counter = app.Behavior("counter");
        var count = counter.State<int>("count", schemaVersion: 1);
        var increment = counter.Command<string, int>("increment", (_, run, _) =>
        {
            run.State.Set(count, run.State.Get(count) + 1);
            return Task.FromResult(run.State.Get(count));
        });
        var invalid = counter.Query<string, int>("invalid", (_, _, ct) => increment.InvokeAsync("", ct));
        var current = counter.Query<string, int>("current", (_, query, _) => Task.FromResult(query.State.Get(count)));
        await app.InstallAsync("counter-one", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "counter-one", stopping.Token);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.QueryAsync("", stopping.Token));
            Assert.Contains("query", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await current.QueryAsync("", stopping.Token));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Query_reads_committed_behavior_state_without_running_a_command()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "queries",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("counter");
        var counter = app.Behavior("counter");
        var count = counter.State<int>("count", schemaVersion: 1);
        var current = counter.Query<string, int>("current", (_, query, _) => Task.FromResult(query.State.Get(count)));
        var increment = counter.Command<int, int>("increment", async (amount, run, ct) =>
        {
            run.State.Set(count, run.State.Get(count) + amount);
            return await run.QueryAsync(current, "", ct);
        });
        var report = app.Command<string, string>("report", async (_, run, ct) =>
            $"Count: {await run.QueryAsync(current, "", ct)}");
        await app.InstallAsync("counter-one", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "counter-one", stopping.Token);
        try
        {
            Assert.Equal(3, await increment.InvokeAsync(3, stopping.Token));
            Assert.Equal(3, await current.QueryAsync("", stopping.Token));
            Assert.Equal("Count: 3", await report.InvokeAsync("", stopping.Token));
            Assert.Equal(3, await current.QueryAsync("", stopping.Token));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }
}

