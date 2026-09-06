using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class WorkspaceInjectTests(SimulationFixture fixture)
{
    [Fact]
    public async Task InjectMain_StampsStoredCorrelationAndReusesIt()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("workspace-owner"));
        var inject = new WorkspaceInject(brain, fixture.Sim.Grains);
        var actor = new ActorContext(new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")), "owner");
        var cancellationToken = TestContext.Current.CancellationToken;

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

        Assert.Equal(firstDelivery.CorrelationId, secondDelivery.CorrelationId);
        Assert.Equal(
            inbox.Id,
            NeuronId.For<IComposer>(brain.Owner, IComposer.DefaultInstanceName));
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
