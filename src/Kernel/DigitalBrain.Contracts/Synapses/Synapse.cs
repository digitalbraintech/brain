using System.Text.Json.Serialization;

using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Synapses;

// A directed, typed edge stored on the SOURCE neuron. Innate and Bound participate in
// broadcast. Learned is a causal observation of a handled send, not a subscription.
[GenerateSerializer]
[Alias("db.synapse")]
public readonly record struct Synapse
{
    [JsonConstructor]
    public Synapse(
        NeuronId source,
        NeuronId target,
        string signalType,
        DateTimeOffset lastFiredAt,
        SynapseKind kind,
        long fireCount = 0,
        bool isBlocking = false,
        CorrelationId? correlation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        ArgumentOutOfRangeException.ThrowIfNegative(fireCount);

        if (isBlocking && kind != SynapseKind.Innate)
        {
            throw new ArgumentException(
                $"Only an innate synapse may block; '{kind}' may not.",
                nameof(isBlocking));
        }

        Source = source;
        Target = target;
        SignalType = signalType;
        LastFiredAt = lastFiredAt;
        Kind = kind;
        FireCount = fireCount;
        IsBlocking = isBlocking;
        Correlation = correlation;
    }

    [Id(0)] public NeuronId Source { get; }
    [Id(1)] public NeuronId Target { get; }
    [Id(2)] public string SignalType { get; }
    [Id(3)] public DateTimeOffset LastFiredAt { get; }
    [Id(4)] public SynapseKind Kind { get; }
    [Id(5)] public long FireCount { get; }
    [Id(6)] public bool IsBlocking { get; }
    [Id(7)] public CorrelationId? Correlation { get; }

    public Synapse RecordHandled(DateTimeOffset now)
        => new(Source, Target, SignalType, now, Kind, FireCount + 1, IsBlocking, Correlation);

    public override string ToString()
        => Correlation is { } correlation
            ? $"{Source} --{SignalType}:{correlation}--> {Target}  fired={FireCount}  {Kind.ToString().ToLowerInvariant()}"
            : $"{Source} --{SignalType}--> {Target}  fired={FireCount}  {Kind.ToString().ToLowerInvariant()}";
}
