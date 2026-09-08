using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// The neuron's own wake-up. Deliver accepts and journals; the drain reacts, in the neuron's
// own turn, to one pending entry per call. One-way: the caller never waits for a reaction.
[Alias("db.v3.neuron-drain")]
public interface INeuronDrain : IGrainWithStringKey
{
    [OneWay]
    [Alias(nameof(Drain))]
    Task Drain();
}
