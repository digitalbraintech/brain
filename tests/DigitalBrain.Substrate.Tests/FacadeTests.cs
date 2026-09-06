using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class FacadeTests
{
    [GenerateSerializer]
    [Alias("db.test.silent-request")]
    public sealed record SilentRequest(string Text) : Signal<SilentResponse>;

    [GenerateSerializer]
    [Alias("db.test.silent-response")]
    public sealed record SilentResponse(string Text) : Signal;

    [Fact]
    public async Task FacadeAndNeuronReferenceQueriesUseImplicitSubjects()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var cancellationToken = TestContext.Current.CancellationToken;
        await brain.Brain.ActivateAsync(cancellationToken);

        var announcerId = new NeuronId("announcer", new OwnerId(DigitalBrainNames.DefaultOwner), "facade");
        var announcer = brain.Grains.GetGrain<IAnnouncer>(announcerId.ToGrainId());
        var announcerReference = brain.Brain.Get<IAnnouncer>("facade");

        await announcer.Announce("hello");

        var rootJournal = await brain.Brain.ReadJournalAsync(
            JournalKind.Outgoing,
            cancellationToken: cancellationToken);
        var announcerJournal = await announcerReference.ReadJournalAsync(
            JournalKind.Outgoing,
            cancellationToken: cancellationToken);
        var synapses = await announcerReference.GetSynapsesAsync(cancellationToken);

        Assert.Contains(rootJournal.Delta, delivery => delivery.Signal is DigitalBrainActivated);
        var announced = Assert.IsType<Announced>(Assert.Single(announcerJournal.Delta).Signal);
        Assert.Equal("hello", announced.Text);
        Assert.DoesNotContain(announcerJournal.Delta, delivery => delivery.Signal is DigitalBrainActivated);
        Assert.Empty(synapses);
        Assert.Empty(await brain.Brain.GetSynapsesAsync(cancellationToken));
    }

    [Fact]
    public async Task SubscribeTo_FromNeuronReference_WritesBoundSynapseOnSource()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var cancellationToken = TestContext.Current.CancellationToken;
        await brain.Brain.ActivateAsync(cancellationToken);

        var announcer = brain.Brain.Get<IAnnouncer>("facade");
        var ear = brain.Brain.Get<IEarA>("listener");

        await ear.SubscribeToAsync<IEarA, IAnnouncer, Announced>(announcer.Id, cancellationToken);

        var synapse = Assert.Single(await announcer.GetSynapsesAsync(cancellationToken));
        Assert.Equal(SynapseKind.Bound, synapse.Kind);
        Assert.Equal(announcer.Id, synapse.Source);
        Assert.Equal(ear.Id, synapse.Target);
        Assert.Equal(nameof(Announced), synapse.SignalType);
        Assert.Equal(
            1,
            await brain.Grains.GetGrain<IAnnouncer>(announcer.Id.ToGrainId()).Announce("hello"));
    }

    [Fact]
    public async Task TypedRequest_UnhandledTargetFailsBeforeWaiting()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var silent = brain.Brain.Get<IPingSilent>("typed-request");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => silent.RequestAsync(new SilentRequest("ignored"), cancellation.Token));

        Assert.False(cancellation.IsCancellationRequested);
        Assert.Contains(nameof(SilentRequest), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledFacadeRequest_DoesNotLearnFromLateRemoteCompletion()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var target = simulation.Brain.Get<IRequestTarget>("facade-cancel");
        await simulation.Brain.ActivateAsync(TestContext.Current.CancellationToken);
        await target.ReadJournalAsync(JournalKind.Outgoing,
            cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => target.RequestAsync(
            new ProbeRequest("late", "ignore-cancellation", 900), cancellation.Token));

        // The next request waits behind the remote handler. A late reply must neither
        // satisfy this request nor reinforce its cancelled predecessor's root route.
        var response = await target.RequestAsync(new ProbeRequest("current", "normal"),
            TestContext.Current.CancellationToken);
        Assert.Equal("current", response.Text);
        var route = Assert.Single(await simulation.Brain.GetSynapsesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(target.Id, route.Target);
        Assert.Equal(1, route.FireCount);
    }

    [Fact]
    public async Task OwnerRoot_RefusesForeignQuerySubjects()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId(DigitalBrainNames.DefaultOwner);
        var root = brain.Grains.GetGrain<IBrainNeuron>(IBrainNeuron.ForOwner(owner).ToGrainId());
        var foreign = new NeuronId("pingsink", new OwnerId("someone-else"), "private");

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => root.ReadNeuronSynapses(foreign));
    }
}
