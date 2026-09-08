using Orleans.Concurrency;

namespace DigitalBrain.Core;

// The neuron's own wake-up, not part of its public surface: Deliver accepts and journals, and
// this one-way self-message tells the neuron to react to one pending entry in its own turn.
internal interface INeuronInbox : IGrainWithStringKey
{
    [OneWay]
    [Alias("db.v3.inbox")]
    Task Drain();
}
