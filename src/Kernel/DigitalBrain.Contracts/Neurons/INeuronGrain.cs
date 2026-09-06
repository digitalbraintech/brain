using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// Orleans delivery and synapse mutation (membrane). Scripts never call this; they Publish /
// Subscribe through IDigitalBrain. SignalSender journals outgoing deliveries and reinforces
// handled paths. Neuron journals incoming deliveries and binds outgoing synapses.
// Grain call filters must not write journals or synapses; self-send stays in-process.
// SubscribeTo asks the source to BindOutgoing.
[Alias("db.v2.neuron-grain")]
public interface INeuronGrain : INeuron
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<DeliveryOutcome> Deliver(
        SignalDelivery delivery,
        CancellationToken cancellationToken = default);

    [Alias(nameof(BindOutgoing))]
    Task BindOutgoing(NeuronId subscriber, string signalType, CorrelationId? correlation = null);

    [Alias(nameof(UnbindOutgoing))]
    Task UnbindOutgoing(NeuronId subscriber, string signalType, CorrelationId? correlation = null);

    [Alias(nameof(FenceSourceEpoch))]
    Task FenceSourceEpoch(NeuronId source, long minimumEpoch);

    [Alias(nameof(FenceSourceStream))]
    Task FenceSourceStream(NeuronId source, string stream, long minimumGeneration);

    [Alias(nameof(Broadcast))]
    Task<int> Broadcast(Signal signal, CancellationToken cancellationToken = default);

    [Alias(nameof(SendFrom))]
    Task<SignalDeliveryResult> SendFrom(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken = default);

    [Alias(nameof(SendFromWithCorrelation))]
    Task<SignalDeliveryResult> SendFromWithCorrelation(
        NeuronId receiver,
        Signal signal,
        CorrelationId correlation,
        CancellationToken cancellationToken = default);
}
