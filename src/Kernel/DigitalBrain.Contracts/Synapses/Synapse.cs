using System.Text.Json.Serialization;

using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Synapses;

// A directed, typed, weighted edge between two neurons. Stored in the SOURCE neuron's durable
// state, never as a grain of its own: an edge per grain does not survive the first million edges.
[GenerateSerializer]
[Alias("db.synapse")]
public readonly record struct Synapse
{
    [JsonConstructor]
    public Synapse(
        NeuronId source,
        NeuronId target,
        string signalType,
        double weight,
        DateTimeOffset lastFiredAt,
        SynapseKind kind,
        long fireCount = 0,
        bool isBlocking = false,
        CorrelationId? correlation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        ArgumentOutOfRangeException.ThrowIfNegative(weight);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weight, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegative(fireCount);

        // Spec D10: a discovered route can never gain veto power over a turn.
        if (isBlocking && kind != SynapseKind.Innate)
        {
            throw new ArgumentException(
                $"Only an innate synapse may block; '{kind}' may not.",
                nameof(isBlocking));
        }

        Source = source;
        Target = target;
        SignalType = signalType;
        Weight = weight;
        LastFiredAt = lastFiredAt;
        Kind = kind;
        FireCount = fireCount;
        IsBlocking = isBlocking;
        Correlation = correlation;
    }

    [Id(0)] public NeuronId Source { get; }
    [Id(1)] public NeuronId Target { get; }
    [Id(2)] public string SignalType { get; }
    [Id(3)] public double Weight { get; }
    [Id(4)] public DateTimeOffset LastFiredAt { get; }
    [Id(5)] public SynapseKind Kind { get; }
    [Id(6)] public long FireCount { get; }
    [Id(7)] public bool IsBlocking { get; }
    [Id(8)] public CorrelationId? Correlation { get; }

    // Read-time decay. There is deliberately no timer and no sweep on the hot path: a synapse
    // nobody uses is already weak the next time anyone looks at it.
    public double WeightAt(DateTimeOffset now, TimeSpan halfLife)
    {
        if (Kind is SynapseKind.Innate or SynapseKind.Bound)
        {
            return Weight;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(halfLife, TimeSpan.Zero);

        var elapsed = now - LastFiredAt;
        return elapsed <= TimeSpan.Zero
            ? Weight
            : Weight * Math.Pow(0.5, elapsed / halfLife);
    }

    // Hebbian: a firing the receiver HANDLED raises the weight asymptotically toward 1 and
    // stamps the instant. An unhandled signal must not call this.
    public Synapse Potentiate(DateTimeOffset now, TimeSpan halfLife, double rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rate, 1.0);

        var effectiveWeight = WeightAt(now, halfLife);

        return new Synapse(
            Source,
            Target,
            SignalType,
            effectiveWeight + (rate * (1.0 - effectiveWeight)),
            now,
            Kind,
            FireCount + 1,
            IsBlocking,
            Correlation);
    }

    public bool IsPrunedAt(DateTimeOffset now, TimeSpan halfLife, double floor)
        => Kind is not (SynapseKind.Innate or SynapseKind.Bound)
            && WeightAt(now, halfLife) < floor;

    public override string ToString()
        => Correlation is { } correlation
            ? $"{Source} --{SignalType}:{correlation}--> {Target}  w={Weight:F2}  fired={FireCount}  {Kind.ToString().ToLowerInvariant()}"
            : $"{Source} --{SignalType}--> {Target}  w={Weight:F2}  fired={FireCount}  {Kind.ToString().ToLowerInvariant()}";
}
