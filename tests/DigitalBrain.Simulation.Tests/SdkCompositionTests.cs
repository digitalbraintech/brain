using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SdkCompositionTests
{
    [Fact]
    public async Task Trusted_connection_scopes_local_names_and_carries_actor_without_leaking_it()
    {
        var token = TestContext.Current.CancellationToken;
        await using var sim = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "sdk-test");
        await using var brain = DigitalBrainClient.Connect(sim.Grains, sim.Brain.Owner.Value, actor);
        Assert.Null(VerifiedActor.Current);
        var behavior = brain.Get<IBehavior>("local-review");
        Assert.Equal(PrincipalPartition.InstanceName(actor.PrincipalId, "local-review"), behavior.Id.Name);
        var saved = await behavior.RequestAsync(new SaveBehaviorScript("return null;", ["Note"], []), token);
        Assert.Equal(actor.PrincipalId, saved.Behavior.Principal);
        Assert.Equal("return null;", saved.Behavior.Draft!.Source);
        Assert.Null(VerifiedActor.Current);
        using var foreign = VerifiedActor.Enter(new(PrincipalId.New(), "other"));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => behavior.ReadAsync(token));
    }
}
