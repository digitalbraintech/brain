using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationApplyTests
{
    [Fact(Timeout = 30000)]
    public async Task Configuration_can_invoke_its_own_staged_commands()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "self-apply",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("home");
        var settings = app.Behavior("settings");
        var count = settings.State<int>("count", schemaVersion: 1);
        var increment = settings.Command<int, int>("increment", (amount, run, _) =>
        {
            run.State.Set(count, run.State.Get(count) + amount);
            return Task.FromResult(run.State.Get(count));
        });
        app.OnApply(async (run, ct) => { await run.InvokeAsync(increment, 1, ct); });
        await simulation.ApplyApplicationAsync(app, "home-one", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "home-one", stopping.Token);
        try { Assert.Equal(2, await increment.InvokeAsync(1, stopping.Token)); }
        finally { await stopping.CancelAsync(); await worker; }
    }

    [Fact(Timeout = 30000)]
    public async Task Failed_configuration_keeps_the_previous_application_active()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "failed-apply", actor);
        var previous = brain.Application("home");
        var reply = previous.Command<string, string>("reply", (_, _, _) => Task.FromResult("old-home"));
        await previous.InstallAsync("old", TestContext.Current.CancellationToken);
        var candidate = brain.Application("home");
        candidate.Command<string, string>("reply", (_, _, _) => Task.FromResult("new-home"));
        candidate.OnApply((_, _) => throw new InvalidOperationException("Configuration failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => simulation.ApplyApplicationAsync(candidate, "new", TestContext.Current.CancellationToken));
        using var verified = VerifiedActor.Enter(actor);
        Assert.Equal("old", await reply.HeadAsync());
    }

    [Fact(Timeout = 30000)]
    public async Task Reapplying_successful_revision_does_not_repeat_configuration()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "apply",
            new ActorContext(PrincipalId.New(), "author"));
        var target = brain.Application("settings");
        var settings = target.Behavior("settings");
        var count = settings.State<int>("count", schemaVersion: 1);
        var configure = settings.Command<string, int>("configure", (_, run, _) =>
        {
            run.State.Set(count, run.State.Get(count) + 1);
            return Task.FromResult(run.State.Get(count));
        });
        await target.InstallAsync("settings-one", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = simulation.ServeApplicationAsync(target, "settings-one", stopping.Token);
        var app = brain.Application("startup");
        app.OnApply(async (run, ct) => { await run.InvokeAsync(configure, "home", ct); });
        try
        {
            await simulation.ApplyApplicationAsync(app, "startup-one", stopping.Token);
            await simulation.ApplyApplicationAsync(app, "startup-one", stopping.Token);
            Assert.Equal(2, await configure.InvokeAsync("next", stopping.Token));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }
}

