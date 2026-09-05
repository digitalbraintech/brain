using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[Alias("DigitalBrain.Substrate.Tests.IMembraneSpy")]
public interface IMembraneSpy : INeuron
{
    [Alias(nameof(ReadForeignSynapses))]
    Task<int> ReadForeignSynapses(NeuronId victim);

    Task<int> ReadForeignBehaviors(NeuronId victim);
}

internal sealed class MembraneSpy(NeuronRuntime runtime) : Neuron(runtime), IMembraneSpy
{
    public async Task<int> ReadForeignSynapses(NeuronId victim)
        => (await GrainFactory.GetGrain<INeuronQuery>(victim.ToGrainId()).ReadSynapses()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)).Count;

    public async Task<int> ReadForeignBehaviors(NeuronId victim)
        => (await GrainFactory.GetGrain<IBehaviorsKernel>(victim.ToGrainId()).ReadBehaviorIds()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)).Count;
}

public sealed class MembraneFilterTests
{
    [Fact]
    public async Task Deliver_FromForeignOwner_IsRefusedAndDoesNotJournal()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "membrane-deliver");
        var target = brain.Grains.GetGrain<INeuronGrain>(targetId.ToGrainId());
        var query = brain.Grains.GetGrain<INeuronQuery>(targetId.ToGrainId());
        var delivery = SignalDelivery.Create(
            new Announced("foreign"),
            new NeuronId("caller", new OwnerId("someone-else"), "source"),
            sequence: 1,
            TimeProvider.System);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => target.Deliver(delivery, TestContext.Current.CancellationToken));

        Assert.Empty((await query.ReadJournal(
            DigitalBrain.Abstractions.Journals.JournalKind.Incoming,
            0)).Delta);
    }

    [Fact]
    public async Task BindOutgoing_FromForeignOwner_IsRefused()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var sourceId = new NeuronId("announcer", new OwnerId("owner"), "membrane-bind");
        var source = brain.Grains.GetGrain<INeuronGrain>(sourceId.ToGrainId());
        var foreign = new NeuronId("eara", new OwnerId("someone-else"), "listener");

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => source.BindOutgoing(foreign, nameof(Announced)));

        Assert.Empty(await brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId()).ReadSynapses());
    }

    [Fact]
    public async Task ClientQuery_StillReadsSameOwnerNeuron()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "membrane-client");
        var query = brain.Grains.GetGrain<INeuronQuery>(targetId.ToGrainId());

        Assert.Empty(await query.ReadSynapses());
    }

    [Fact]
    public async Task GrainToGrainQuery_ForeignOwner_IsRefused()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var spy = brain.Grains.GetGrain<IMembraneSpy>(
            new NeuronId("membranespy", new OwnerId("owner"), "spy").ToGrainId());
        var victim = new NeuronId("eara", new OwnerId("someone-else"), "private");

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => spy.ReadForeignSynapses(victim));
    }

    [Fact]
    public async Task BehaviorQuery_ForeignOwner_IsRefused()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var spy = brain.Grains.GetGrain<IMembraneSpy>(
            new NeuronId("membranespy", new OwnerId("owner"), "behavior-spy").ToGrainId());
        var victim = NeuronId.For<IBehaviors>(new OwnerId("someone-else"), "default");

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => spy.ReadForeignBehaviors(victim));
    }

    [Fact]
    public async Task GrainToGrainQuery_SameOwner_IsAllowed()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId("owner");
        var spy = brain.Grains.GetGrain<IMembraneSpy>(
            new NeuronId("membranespy", owner, "spy-same").ToGrainId());
        var peer = new NeuronId("eara", owner, "peer");

        Assert.Equal(0, await spy.ReadForeignSynapses(peer));
    }
}
