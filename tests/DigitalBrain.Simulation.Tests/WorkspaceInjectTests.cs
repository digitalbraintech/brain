using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class WorkspaceInjectTests(SimulationFixture fixture)
{
    [Fact]
    public async Task InjectMain_UsesDistinctActivityCorrelations()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("workspace-owner"));
        var inject = new WorkspaceInject(brain, fixture.Sim.Grains);
        var actor = new ActorContext(new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")), "owner");
        var cancellationToken = TestContext.Current.CancellationToken;
        using var verified = VerifiedActor.Enter(actor);

        var first = await inject.InjectUserMessage("main", "hello main", actor, cancellationToken);
        var second = await inject.InjectUserMessage("main", "hello again", actor, cancellationToken);

        var inbox = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var firstDelivery = await JournalWait.ForAsync(
            inbox,
            JournalKind.Incoming,
            delivery => delivery.Signal is UserMessaged message && message.CommandId == first,
            cancellationToken: cancellationToken);
        var secondDelivery = await JournalWait.ForAsync(
            inbox,
            JournalKind.Incoming,
            delivery => delivery.Signal is UserMessaged message && message.CommandId == second,
            cancellationToken: cancellationToken);

        Assert.NotEqual(firstDelivery.CorrelationId, secondDelivery.CorrelationId);
        foreach (var delivery in new[] { firstDelivery, secondDelivery })
        {
            var broadcast = await JournalWait.ForAsync(
                inbox,
                JournalKind.Outgoing,
                item => item.CausationId == delivery.SignalId,
                cancellationToken: cancellationToken);

            Assert.Equal(delivery.CorrelationId, broadcast.CorrelationId);
            Assert.Equal(actor.PrincipalId, delivery.Principal);
            Assert.Equal(actor.PrincipalId, broadcast.Principal);
            Assert.Equal(IBrainNeuron.ForOwner(brain.Owner), delivery.Caller);
            Assert.Equal(inbox.Id, broadcast.Caller);
        }
    }

    [Fact]
    public async Task InjectMain_HonorsCallerCommandId()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("workspace-command"));
        var inject = new WorkspaceInject(brain, fixture.Sim.Grains);
        var actor = new ActorContext(new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")), "owner");
        var command = CommandId.New();
        var cancellationToken = TestContext.Current.CancellationToken;

        var returned = await inject.InjectUserMessage("main", "reuse this id", actor, cancellationToken, command);

        Assert.Equal(command, returned);
        var inbox = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var delivery = await JournalWait.ForAsync(
            inbox,
            JournalKind.Incoming,
            item => item.Signal is UserMessaged message && message.CommandId == command,
            cancellationToken: cancellationToken);
        Assert.Equal(command, ((UserMessaged)delivery.Signal).CommandId);
    }

    [Fact]
    public async Task InjectUnknownWorkspace_IsRefused()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("workspace-unknown"));
        var inject = new WorkspaceInject(brain, fixture.Sim.Grains);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
            inject.InjectUserMessage(
                "taxes",
                "nope",
                new ActorContext(new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")), "owner"),
                TestContext.Current.CancellationToken));
    }
}
