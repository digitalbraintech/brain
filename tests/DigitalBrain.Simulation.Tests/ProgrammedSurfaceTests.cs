using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class ProgrammedSurfaceTests(SimulationFixture fixture)
{
    [Fact]
    public async Task Startup_can_subscribe_renderer_before_opening_its_scene()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("startup-surface"));
        var token = TestContext.Current.CancellationToken;
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        var activities = brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var execution = brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        await activities.SubscribeToAsync<IActivities, IActivitySource, ActivityExecutionChanged>(execution.Id, token);
        await renderer.SubscribeToAsync<IUIRenderer, IActivities, ActivityChanged>(activities.Id, token);
        await renderer.SendAsync(new OpenSurface(CommandId.New(), "home", "One brain",
            new SurfaceComponent("split")), token).WaitAsync(TimeSpan.FromSeconds(10), token);
        await JournalWait.ForAsync(renderer, JournalKind.Outgoing,
            item => item.Signal is ActivityChanged { Activity.TriggerName: nameof(OpenSurface), Activity.Status: "completed" },
            TimeSpan.FromSeconds(15), cancellationToken: token);
        var state = await brain.GetEntity<ISurface>(ISurface.DefaultInstanceName).Read();
        Assert.Equal("split", Assert.Single(state!.Scenes).Root!.Kind);
    }

    [Fact]
    public async Task Scripted_renderer_subscription_updates_surface_and_keeps_composition()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("programmed-surface"));
        var renderer = brain.Get<IUIRenderer>("desk");
        var activities = brain.Get<IActivities>("activities");
        var root = new SurfaceComponent("split", Children:
        [new SurfaceComponent("brain-graph"), new SurfaceComponent("chat")]);
        await renderer.SendAsync(new OpenSurface(CommandId.New(), "home", "One brain", root), TestContext.Current.CancellationToken);
        await JournalWait.ForAsync(renderer, JournalKind.Outgoing,
            delivery => delivery.Signal is SurfaceOpened, cancellationToken: TestContext.Current.CancellationToken);
        await renderer.SubscribeToAsync<IUIRenderer, IActivities, ActivityChanged>(activities.Id, TestContext.Current.CancellationToken);
        var source = NeuronId.For<IActivitySource>(brain.Owner, "execution");
        var correlation = CorrelationId.New();
        await activities.SendAsync(new ActivityExecutionChanged(correlation, null, "example",
            SignalId.New(), null, source, source, "UserMessage", "running", DateTimeOffset.UtcNow, "Research on Orleans"), TestContext.Current.CancellationToken);
        await JournalWait.ForAsync(renderer, JournalKind.Outgoing,
            delivery => delivery.Signal is ActivityChanged change && change.Activity.CorrelationId == correlation.ToString(),
            cancellationToken: TestContext.Current.CancellationToken);
        var state = await brain.GetEntity<ISurface>("desk").Read();
        Assert.Equal("split", Assert.Single(state!.Scenes).Root!.Kind);
        Assert.Equal("Research on Orleans", Assert.Single(state.Activities!).Title);
    }
}
