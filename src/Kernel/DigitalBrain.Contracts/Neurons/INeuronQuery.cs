using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

/// <summary>
/// Observes one neuron's durable traffic and graph without creating signal traffic.
/// </summary>
[Alias("db.v2.neuron-query")]
public interface INeuronQuery : IGrainWithStringKey
{
    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();

    [Alias(nameof(Watch))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Watch(JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias(nameof(Unwatch))]
    Task Unwatch(IJournalObserver observer);
}
