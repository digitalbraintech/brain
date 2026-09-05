using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// A source neuron's synapses: binding, reinforcement, and decay. Each target+signal pair
// identifies one connection in the neuron's durable state.
internal sealed class NeuronSynapses
{
    private readonly IDurableDictionary<string, Synapse> _synapses;
    private readonly SynapseOptions _options;
    private readonly TimeProvider _time;
    private readonly NeuronId _source;

    internal NeuronSynapses(
        IDurableDictionary<string, Synapse> synapses,
        SynapseOptions options,
        NeuronId source,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(synapses);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        _synapses = synapses;
        _options = options;
        _source = source;
        _time = time;
    }

    internal static string KeyFor(NeuronId target, string signalType, CorrelationId? correlation = null)
        => correlation is { } id
            ? $"{target} {signalType} {id}"
            : $"{target} {signalType}";

    // Slice 1 pruning is read/routing exclusion. Physical reclamation belongs to a later
    // storage-maintenance decision. Returned records retain stored Weight and are ordered by
    // effective decayed strength; callers can use WeightAt when they need the current value.
    internal IReadOnlyList<Synapse> Active()
    {
        var now = _time.GetUtcNow();

        return
        [
            .. _synapses.Values
                .Where(synapse => !synapse.IsPrunedAt(now, _options.HalfLife, _options.PruneFloor))
                .OrderByDescending(synapse => synapse.WeightAt(now, _options.HalfLife))
        ];
    }

    // Slice 1 pruning is read/routing exclusion. Physical reclamation belongs to a later
    // storage-maintenance decision.
    internal IReadOnlyList<Synapse> ForSignal(string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();

        return
        [
            .. _synapses.Values
                .Where(synapse => string.Equals(synapse.SignalType, signalType, StringComparison.Ordinal))
                .Where(synapse => !synapse.IsPrunedAt(now, _options.HalfLife, _options.PruneFloor))
                .OrderByDescending(synapse => synapse.WeightAt(now, _options.HalfLife))
        ];
    }

    // A successful fire creates or strengthens its synapse. Call only after the receiver
    // handled the signal; an unhandled delivery must not reinforce its path.
    internal Synapse Reinforce(NeuronId target, string signalType, SynapseKind kind, CorrelationId? correlation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();
        var key = KeyFor(target, signalType, correlation);

        var current = _synapses.TryGetValue(key, out var existing)
            ? existing
            : new Synapse(
                _source,
                target,
                signalType,
                _options.InitialWeightFor(kind),
                now,
                kind,
                isBlocking: false,
                correlation: correlation);

        var potentiated = current.Potentiate(now, _options.HalfLife, _options.PotentiationRate);

        _synapses[key] = potentiated;
        return potentiated;
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
                existing.Kind is SynapseKind.Bound or SynapseKind.Innate
                    ? existing.Weight
                    : _options.InnateWeight,
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
            _options.InnateWeight,
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
