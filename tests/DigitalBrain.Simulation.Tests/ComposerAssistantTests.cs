using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class ComposerAssistantTests(SimulationFixture fixture)
{
    [Fact]
    public async Task DirectedUserMessagedReachesInoAndDoesNotAsk()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("composer-owner"));
        var inbox = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var ino = brain.Get<IAssistant>("assistant");
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal(new NeuronId(IComposer.GrainTypeName, brain.Owner, IComposer.DefaultInstanceName), inbox.Id);

        await ino.SubscribeToAsync<IComposer, UserMessaged>(inbox.Id, cancellationToken);

        var command = CommandId.New();
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");
        await inbox.SendAsync(new UserMessaged(command, inbox.Id, "hello inbox", actor), cancellationToken);

        var incoming = await JournalWait.ForAsync(
            ino,
            JournalKind.Incoming,
            delivery => delivery.Signal is UserMessaged message && message.CommandId == command,
            cancellationToken: cancellationToken);
        Assert.Equal("hello inbox", ((UserMessaged)incoming.Signal).Text);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        var outgoing = await ino.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: cancellationToken);
        Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Signal is AgentActivity);

        var workerId = ChatTurnWorker.ForChat(new NeuronId("chat", brain.Owner, "main"));
        var worker = fixture.Sim.Grains.GetGrain<INeuronQuery>(workerId.ToGrainId());
        var workerJournal = await worker.ReadJournal(JournalKind.Outgoing, 0)
            .WaitAsync(cancellationToken);
        Assert.DoesNotContain(workerJournal.Delta, delivery => delivery.Signal is AgentActivity);
    }
}
