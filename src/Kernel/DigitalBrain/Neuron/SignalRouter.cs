using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Core;

// Broadcast audience is this neuron's synapses of that signal type. IHandle is the
// capability to receive; it does not subscribe every instance of a type.
public sealed class SignalRouter
{
    internal IReadOnlyList<NeuronId> BroadcastRecipientsFor(
        Signal signal,
        NeuronId source,
        NeuronSynapses synapses,
        CorrelationId? envelope = null)
        => Matching(signal, source, synapses, envelope).Select(synapse => synapse.Target).ToArray();

    internal IReadOnlyList<Synapse> Matching(
        Signal signal,
        NeuronId source,
        NeuronSynapses synapses,
        CorrelationId? envelope = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(synapses);

        var matched = new List<Synapse>();
        // Seeded with self: a broadcaster must never receive its own broadcast.
        var seen = new HashSet<NeuronId> { source };
        foreach (var synapse in synapses.ForSignal(signal.GetType().Name))
        {
            if (synapse.Kind is not (SynapseKind.Bound or SynapseKind.Innate))
            {
                continue;
            }

            if (envelope is { } id && synapse.Correlation is { } scoped && scoped != id)
            {
                continue;
            }

            if (seen.Add(synapse.Target))
            {
                matched.Add(synapse);
            }
        }

        return matched;
    }
}
