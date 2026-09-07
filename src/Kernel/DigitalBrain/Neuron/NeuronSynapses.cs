using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class NeuronSynapses(IDurableDictionary<string, Synapse> synapses, NeuronId source, TimeProvider clock)
{
    private static string KeyFor(NeuronId target, string signalType) => $"{target} {signalType}";

    internal IReadOnlyList<Synapse> All() => [.. synapses.Values];

    internal IReadOnlyList<Synapse> ForType(string signalType)
        => [.. synapses.Values.Where(s => string.Equals(s.SignalType, signalType, StringComparison.Ordinal))];

    internal bool Has(NeuronId target, string signalType) => synapses.ContainsKey(KeyFor(target, signalType));

    // Idempotent: connecting twice is one synapse.
    internal bool Connect(NeuronId target, string signalType)
    {
        var key = KeyFor(target, signalType);
        if (synapses.ContainsKey(key))
        {
            return false;
        }

        synapses[key] = new Synapse(source, target, signalType, clock.GetUtcNow());
        return true;
    }

    // No-op when absent.
    internal bool Disconnect(NeuronId target, string signalType) => synapses.Remove(KeyFor(target, signalType));
}
