using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// Read is a query: nothing moves, nothing is journaled.
[Alias("db.v3.neuron-query")]
public interface INeuronQuery : IGrainWithStringKey
{
    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadState))]
    Task<IReadOnlyList<SignalDelivery>> ReadState();

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
}
