using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class NeuronSynapses
{
    private readonly IDurableDictionary<string, Synapse> _synapses;
    private readonly TimeProvider _time;
    private readonly NeuronId _source;

    internal NeuronSynapses(
        IDurableDictionary<string, Synapse> synapses,
        NeuronId source,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(synapses);
        ArgumentNullException.ThrowIfNull(time);

        _synapses = synapses;
        _source = source;
        _time = time;
    }

    internal static string KeyFor(NeuronId target, string signalType, CorrelationId? correlation = null)
        => correlation is { } id
            ? $"{target} {signalType} {id}"
            : $"{target} {signalType}";

    internal IReadOnlyList<Synapse> Active()
        => [.. _synapses.Values];

    internal IReadOnlyList<Synapse> ForSignal(string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        return
        [
            .. _synapses.Values
                .Where(synapse => string.Equals(synapse.SignalType, signalType, StringComparison.Ordinal))
        ];
    }

    internal Synapse Reinforce(NeuronId target, string signalType, SynapseKind kind, CorrelationId? correlation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();
        var key = KeyFor(target, signalType, correlation);
        var current = _synapses.TryGetValue(key, out var existing)
            ? existing
            : new Synapse(_source, target, signalType, now, kind, correlation: correlation);

        var recorded = current.RecordHandled(now);
        _synapses[key] = recorded;
        return recorded;
    }

    internal Synapse Bind(NeuronId target, string signalType, CorrelationId? correlation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();
        var key = KeyFor(target, signalType, correlation);
        if (_synapses.TryGetValue(key, out var existing))
        {
            _synapses[key] = new Synapse(
                existing.Source,
                existing.Target,
                existing.SignalType,
                existing.LastFiredAt,
                SynapseKind.Bound,
                existing.FireCount,
                isBlocking: false,
                existing.Correlation);
            return _synapses[key];
        }

        var bound = new Synapse(
            _source,
            target,
            signalType,
            now,
            SynapseKind.Bound,
            isBlocking: false,
            correlation: correlation);
        _synapses[key] = bound;
        return bound;
    }

    internal void Unbind(NeuronId target, string signalType, CorrelationId? correlation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        _synapses.Remove(KeyFor(target, signalType, correlation));
    }
}
