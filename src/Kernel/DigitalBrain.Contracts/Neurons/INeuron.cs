using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// The two verbs, plus Deliver, which only another neuron's Fire calls.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    // to == null: along every synapse of signal.Type. to != null: along exactly that synapse,
    // creating it first if missing. Returns the number of neurons delivered to.
    [Alias(nameof(Fire))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<int> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default);

    [Alias(nameof(Connect))]
    Task Connect(NeuronId target, string signalType);

    [Alias(nameof(Disconnect))]
    Task Disconnect(NeuronId target, string signalType);

    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);
}
