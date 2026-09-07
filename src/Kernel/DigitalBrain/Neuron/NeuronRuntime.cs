using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

public sealed class NeuronRuntime(TimeProvider clock)
{
    internal TimeProvider Clock { get; } = clock;

    internal NeuronActivationComponents Bind(IServiceProvider services, NeuronId neuronId)
    {
        var entries = services.GetRequiredService<Serializer<JournalEntry>>();
        var sessions = services.GetRequiredService<SerializerSessionPool>();
        JournalWindow Window(string name) => new(
            services.GetRequiredKeyedService<IDurableList<byte[]>>(name),
            services.GetRequiredKeyedService<IDurableDictionary<string, long>>($"{name}.tally"),
            services.GetRequiredKeyedService<IDurableValue<long>>($"{name}.sequence"),
            entries,
            sessions);

        return new(
            Clock,
            new NeuronJournals(Window("incoming"), Window("outgoing")),
            new NeuronSynapses(services.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>("synapses"), neuronId, Clock),
            services.GetRequiredKeyedService<IDurableDictionary<string, SignalDelivery>>("latest"));
    }
}

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    NeuronJournals Journals,
    NeuronSynapses Synapses,
    IDurableDictionary<string, SignalDelivery> Latest);
