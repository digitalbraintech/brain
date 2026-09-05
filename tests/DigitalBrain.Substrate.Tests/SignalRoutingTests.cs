using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.announced")]
public sealed record Announced(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.unheard")]
public sealed record Unheard(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.rumor")]
public sealed record Rumor(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.faulting")]
public sealed record Faulting(string Message) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IAnnouncer")]
public interface IAnnouncer : INeuron
{
    [Alias(nameof(Announce))]
    Task<int> Announce(string text);

    [Alias(nameof(AnnounceUnheard))]
    Task<int> AnnounceUnheard(string text);
}

[Alias("DigitalBrain.Substrate.Tests.IEarA")]
public interface IEarA : INeuron, IHandle<Announced>, IHandle<Faulting>
{
    [Alias(nameof(SubscribeToAnnouncer))]
    Task SubscribeToAnnouncer(NeuronId announcer);

    [Alias(nameof(UnsubscribeFromAnnouncer))]
    Task UnsubscribeFromAnnouncer(NeuronId announcer);
}

[Alias("DigitalBrain.Substrate.Tests.IEarB")]
public interface IEarB : INeuron, IHandle<Announced>;

[Alias("DigitalBrain.Substrate.Tests.IGossip")]
public interface IGossip : INeuron
{
    [Alias(nameof(Spread))]
    Task<int> Spread(string text);
}

[Alias("DigitalBrain.Substrate.Tests.IEarC")]
public interface IEarC : INeuron, IHandle<Rumor>;

internal sealed class Announcer(NeuronRuntime runtime) : Neuron(runtime), IAnnouncer
{
    public Task<int> Announce(string text) => BroadcastAsync(new Announced(text));

    public Task<int> AnnounceUnheard(string text) => BroadcastAsync(new Unheard(text));
}

internal sealed class EarA(NeuronRuntime runtime) : Neuron(runtime), IEarA, IHandle<Announced>, IHandle<Faulting>
{
    public Task SubscribeToAnnouncer(NeuronId announcer)
        => SubscribeToAsync<IAnnouncer, Announced>(announcer);

    public Task UnsubscribeFromAnnouncer(NeuronId announcer)
        => UnsubscribeFromAsync<IAnnouncer, Announced>(announcer);

    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HandleAsync(Faulting signal, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException(signal.Message));
}

internal sealed class EarB(NeuronRuntime runtime) : Neuron(runtime), IEarB, IHandle<Announced>
{
    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

// Declares IHandle<Rumor> for the very signal it broadcasts — the regression fixture for the
// self-receiver deadlock: broadcast excludes self instead of using the directed sender's
// local-delivery shortcut, so if the emitter's own grain type were left in its own
// receiver set, a non-reentrant activation would await a Deliver call into itself and hang
// for the full call timeout.
internal sealed class Gossip(NeuronRuntime runtime) : Neuron(runtime), IGossip, IHandle<Rumor>
{
    public Task<int> Spread(string text) => BroadcastAsync(new Rumor(text));

    public Task HandleAsync(Rumor signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EarC(NeuronRuntime runtime) : Neuron(runtime), IEarC, IHandle<Rumor>
{
    public Task HandleAsync(Rumor signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SignalRoutingTests
{
    private static IAnnouncer AnnouncerIn(BrainSimulation brain, string name)
        => brain.Grains.GetGrain<IAnnouncer>(
            AnnouncerId(name).ToGrainId());

    private static NeuronId AnnouncerId(string name)
        => new("announcer", new OwnerId("owner"), name);

    private static INeuronQuery Query(BrainSimulation brain, NeuronId id)
        => brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

    [Fact]
    public async Task Deliver_WhenHandlerExists_ReturnsHandledAndJournalsIncoming()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "handled");
        var target = brain.Grains.GetGrain<INeuronGrain>(targetId.ToGrainId());
        var query = Query(brain, targetId);
        var delivery = SignalDelivery.Create(
            new Announced("heard"),
            new NeuronId("caller", new OwnerId("owner"), "source"),
            sequence: 1,
            TimeProvider.System);

        var outcome = await target.Deliver(delivery, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryOutcome.Handled, outcome);
        var journaled = Assert.Single((await query.ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(delivery.SignalId, journaled.SignalId);
    }

    [Fact]
    public async Task Deliver_WhenHandlerIsMissing_ReturnsUnhandledAndJournalsIncoming()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "unhandled");
        var target = brain.Grains.GetGrain<INeuronGrain>(targetId.ToGrainId());
        var query = Query(brain, targetId);
        var delivery = SignalDelivery.Create(
            new Unheard("ignored"),
            new NeuronId("caller", new OwnerId("owner"), "source"),
            sequence: 1,
            TimeProvider.System);

        var outcome = await target.Deliver(delivery, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryOutcome.Unhandled, outcome);
        var journaled = Assert.Single((await query.ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(delivery.SignalId, journaled.SignalId);
    }

    [Fact]
    public async Task Deliver_WhenHandlerThrows_PropagatesAndDoesNotJournalIncoming()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "faulted");
        var target = brain.Grains.GetGrain<INeuronGrain>(targetId.ToGrainId());
        var query = Query(brain, targetId);
        var delivery = SignalDelivery.Create(
            new Faulting("sentinel failure"),
            new NeuronId("caller", new OwnerId("owner"), "source"),
            sequence: 1,
            TimeProvider.System);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => target.Deliver(delivery, TestContext.Current.CancellationToken));

        Assert.Equal("sentinel failure", failure.Message);
        Assert.Empty((await query.ReadJournal(JournalKind.Incoming, 0)).Delta);
    }

    private static IGossip GossipDefault(BrainSimulation brain)
        => brain.Grains.GetGrain<IGossip>(
            new NeuronId("gossip", new OwnerId("owner"), "default").ToGrainId());

    [Fact]
    public async Task Broadcast_WithoutSynapsesReachesNobodyEvenWhenTypesHandleTheSignal()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcerId = AnnouncerId("a");

        Assert.Equal(0, await AnnouncerIn(brain, "a").Announce("hello"));
        Assert.Single((await Query(brain, announcerId).ReadJournal(JournalKind.Outgoing, 0)).Delta);
    }

    [Fact]
    public async Task SubscribeThenBroadcast_ReachesOnlyBoundReceivers()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId("owner");
        var announcerId = AnnouncerId("b");
        await brain.Grains.GetGrain<IEarA>(new NeuronId("eara", owner, "default").ToGrainId())
            .SubscribeToAnnouncer(announcerId);
        await brain.Grains.GetGrain<IEarB>(new NeuronId("earb", owner, "default").ToGrainId())
            .HandleAsync(new Subscribe(announcerId, nameof(Announced)), TestContext.Current.CancellationToken);

        var query = Query(brain, announcerId);
        Assert.Equal(2, await AnnouncerIn(brain, "b").Announce("hello"));

        var synapses = await query.ReadSynapses();
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(SynapseKind.Bound, synapse.Kind));
        Assert.All(synapses, synapse => Assert.Equal(nameof(Announced), synapse.SignalType));
        Assert.All(synapses, synapse => Assert.Equal(1, synapse.FireCount));
    }

    [Fact]
    public async Task Broadcast_JournalsOneOutgoingEntryForTheAudience()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId("owner");
        var announcerId = AnnouncerId("d");
        var earA = new NeuronId("eara", owner, "default");
        var earB = new NeuronId("earb", owner, "default");
        await brain.Grains.GetGrain<IEarA>(earA.ToGrainId())
            .HandleAsync(new Subscribe(announcerId, nameof(Announced)), TestContext.Current.CancellationToken);
        await brain.Grains.GetGrain<IEarB>(earB.ToGrainId())
            .HandleAsync(new Subscribe(announcerId, nameof(Announced)), TestContext.Current.CancellationToken);

        var announcer = AnnouncerIn(brain, "d");
        var query = Query(brain, announcerId);

        await announcer.Announce("hello");

        var outgoing = Assert.Single((await query.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        var incomingA = Assert.Single((await Query(brain, earA).ReadJournal(JournalKind.Incoming, 0)).Delta);
        var incomingB = Assert.Single((await Query(brain, earB).ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(outgoing.SignalId, incomingA.SignalId);
        Assert.Equal(outgoing.SignalId, incomingB.SignalId);
        Assert.Equal(outgoing.CorrelationId, incomingA.CorrelationId);
        Assert.Equal(outgoing.CorrelationId, incomingB.CorrelationId);
    }

    [Fact]
    public async Task Broadcast_WithNoDeclaredHandlerStillRecordsTheOutgoingFact()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "e");
        var query = Query(brain, AnnouncerId("e"));

        Assert.Equal(0, await announcer.AnnounceUnheard("hello"));
        Assert.Empty(await query.ReadSynapses());
        Assert.Single((await query.ReadJournal(JournalKind.Outgoing, 0)).Delta);
    }

    [Fact]
    public async Task Broadcast_ExcludesTheEmitterEvenWhenTheEmitterDeclaresIHandleForTheSameSignal()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var gossip = GossipDefault(brain);
        var gossipId = new NeuronId("gossip", new OwnerId("owner"), "default");
        var query = Query(brain, gossipId);

        var reached = await gossip.Spread("hello");

        // No Bound/Learned synapses: IHandle<Rumor> on gossip/EarC does not subscribe them.
        Assert.Equal(0, reached);
        Assert.Empty(await query.ReadSynapses());
    }

    [Fact]
    public async Task Unsubscribe_RemovesBoundSynapseAndBroadcastStops()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId("owner");
        var announcerId = AnnouncerId("u");
        var ear = brain.Grains.GetGrain<IEarA>(new NeuronId("eara", owner, "default").ToGrainId());
        await ear.SubscribeToAnnouncer(announcerId);

        Assert.Equal(1, await AnnouncerIn(brain, "u").Announce("first"));
        await ear.UnsubscribeFromAnnouncer(announcerId);
        Assert.Equal(0, await AnnouncerIn(brain, "u").Announce("second"));
        Assert.Empty(await Query(brain, announcerId).ReadSynapses());
    }
}
