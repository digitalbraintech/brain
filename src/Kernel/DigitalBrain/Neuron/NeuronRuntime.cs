using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

public sealed class NeuronRuntime
{
    public NeuronRuntime(TimeProvider clock, SignalRouter router)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(router);

        Clock = clock;
        Router = router;
    }

    internal TimeProvider Clock { get; }

    internal SignalRouter Router { get; }

    internal SignalDispatcher Dispatcher { get; } = new();

    internal NeuronActivationComponents Bind(
        IServiceProvider activationServices,
        NeuronId neuronId)
    {
        ArgumentNullException.ThrowIfNull(activationServices);

        var entries = activationServices.GetRequiredService<Serializer<JournalEntry>>();
        var incoming = Window("incoming");
        var outgoing = Window("outgoing");
        var journals = new NeuronJournals(neuronId, incoming, outgoing);
        var synapses = new NeuronSynapses(
            activationServices.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>("synapses"),
            neuronId,
            Clock);

        return new(Clock, Router, journals, synapses, Dispatcher);

        JournalWindow Window(string name) => new(
            activationServices.GetRequiredKeyedService<IDurableList<byte[]>>(name),
            activationServices.GetRequiredKeyedService<IDurableDictionary<string, long>>($"{name}.tally"),
            activationServices.GetRequiredKeyedService<IDurableValue<long>>($"{name}.sequence"),
            entries,
            activationServices.GetRequiredService<SerializerSessionPool>());
    }
}

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    SignalRouter Router,
    NeuronJournals Journals,
    NeuronSynapses Synapses,
    SignalDispatcher Dispatcher);
