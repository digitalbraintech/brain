using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions.Neurons;

[Alias("db.v2.brain-neuron")]
public interface IBrainNeuron : INeuron, INeuronQuery
{
    const string GrainTypeName = "sessionneuron";
    const string InstanceName = "session";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    [Alias(nameof(Activate))]
    Task Activate();

    [Alias(nameof(Send))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SignalDeliveryResult> Send(NeuronId receiver, Signal signal, CancellationToken cancellationToken = default);

    [Alias(nameof(SendWithCorrelation))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SignalDeliveryResult> SendWithCorrelation(
        NeuronId receiver,
        Signal signal,
        CorrelationId correlation,
        CancellationToken cancellationToken = default);

    [Alias(nameof(ReadNeuronJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadNeuronJournal(NeuronId subject, JournalKind kind, long afterSequence);

    [Alias(nameof(ReadNeuronSynapses))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<IReadOnlyList<Synapse>> ReadNeuronSynapses(NeuronId subject);

    [Alias(nameof(WatchNeuron))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task WatchNeuron(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias(nameof(UnwatchNeuron))]
    Task UnwatchNeuron(NeuronId subject, IJournalObserver observer);
}
