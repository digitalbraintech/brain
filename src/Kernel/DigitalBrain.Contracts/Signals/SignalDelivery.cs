using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

// The envelope around a Signal. Identity, causation, correlation and source ride here.
[GenerateSerializer]
[Alias("db.signal-delivery")]
public sealed record SignalDelivery(
    [property: Id(0)] Signal Signal,
    [property: Id(1)] SignalId SignalId,
    [property: Id(2)] CorrelationId CorrelationId,
    [property: Id(3)] SignalId? CausationId,
    [property: Id(4)] NeuronId Source,
    [property: Id(5)] long Sequence,
    [property: Id(6)] DateTimeOffset Timestamp)
{
    public static SignalDelivery Create(
        Signal signal,
        NeuronId source,
        long sequence,
        TimeProvider clock,
        SignalDelivery? cause = null,
        CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return new(
            signal,
            SignalId.New(),
            correlation ?? cause?.CorrelationId ?? CorrelationId.New(),
            cause?.SignalId,
            source,
            sequence,
            clock.GetUtcNow());
    }
}
