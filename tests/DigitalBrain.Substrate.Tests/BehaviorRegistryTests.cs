using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class BehaviorRegistryTests
{
    [Fact]
    public async Task Saved_behavior_index_and_source_survive_activation_collection()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "registry-test");
        await using var brain = DigitalBrainClient.Connect(sim.Grains, sim.Brain.Owner.Value, actor);
        var behavior = brain.Get<IBehavior>("saved-program");
        var saved = await behavior.RequestAsync(new SaveBehaviorScript("return null;", [nameof(NewPost)], []), TestContext.Current.CancellationToken);
        await sim.Grains.GetGrain<Orleans.Runtime.IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);
        var registry = sim.Grains.GetGrain<IBehaviorsKernel>(brain.Get<IBehaviors>().Id.ToGrainId());
        Assert.Contains(behavior.Id, await registry.ReadBehaviorIds());
        var restored = await behavior.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(saved.Behavior.Draft!.Revision, restored.Draft!.Revision);
        Assert.Equal("return null;", restored.Draft.Source);
        Assert.Equal(actor.PrincipalId, restored.Principal);
    }

    [Fact]
    public async Task Registry_refuses_registration_outside_the_verified_principal()
    {
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var principal = PrincipalId.New();
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "registry-test"));
        var registry = sim.Grains.GetGrain<IBehaviorsKernel>(sim.Brain.Get<IBehaviors>().Id.ToGrainId());
        var foreign = NeuronId.For<IBehavior>(sim.Brain.Owner, PrincipalPartition.InstanceName(PrincipalId.New(), "foreign-program"));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => registry.RegisterBehavior(foreign));
        Assert.DoesNotContain(foreign, await registry.ReadBehaviorIds());
    }
}
