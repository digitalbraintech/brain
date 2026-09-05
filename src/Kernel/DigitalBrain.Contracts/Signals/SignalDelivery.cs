using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.signal-delivery")]
public sealed class SignalDelivery
{
    internal SignalDelivery(
        Signal signal,
        SignalId signalId,
        CorrelationId correlationId,
        SignalId? causationId,
        NeuronId caller,
        long sequence,
        DateTimeOffset timestamp,
        PrincipalId? principal = null,
        long? sourceEpoch = null,
        string? sourceStream = null,
        long? streamGeneration = null)
    {
        Signal = signal;
        SignalId = signalId;
        CorrelationId = correlationId;
        CausationId = causationId;
        Caller = caller;
        Sequence = sequence;
        Timestamp = timestamp;
        Principal = principal;
        SourceEpoch = sourceEpoch;
        SourceStream = sourceStream;
        StreamGeneration = streamGeneration;
    }

    [Id(0)]
    public Signal Signal { get; }

    [Id(1)]
    public SignalId SignalId { get; }

    [Id(2)]
    public CorrelationId CorrelationId { get; }

    [Id(3)]
    public SignalId? CausationId { get; }

    [Id(4)]
    public NeuronId Caller { get; }

    [Id(5)]
    public long Sequence { get; }

    [Id(6)]
    public DateTimeOffset Timestamp { get; }

    // Trailing: rides the delivery so Neuron.DispatchDeliveryAsync can re-enter VerifiedActor.
    // Null = system/unattributed (timer ticks, bootstrap). Append-only — never renumber.
    [Id(7)]
    public PrincipalId? Principal { get; }

    [Id(8)]
    public long? SourceEpoch { get; }

    [Id(9)]
    public string? SourceStream { get; }

    [Id(10)]
    public long? StreamGeneration { get; }

    public static SignalDelivery Create(
        Signal signal,
        NeuronId caller,
        long sequence,
        TimeProvider timeProvider,
        SignalDelivery? cause = null,
        CorrelationId? correlation = null,
        PrincipalId? principal = null,
        SignalId? signalId = null,
        long? sourceEpoch = null,
        string? sourceStream = null,
        long? streamGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);

        return new(
            signal,
            signalId ?? SignalId.New(),
            correlation ?? cause?.CorrelationId ?? CorrelationId.New(),
            cause?.SignalId,
            caller,
            sequence,
            timeProvider.GetUtcNow(),
            principal ?? cause?.Principal,
            sourceEpoch,
            sourceStream,
            streamGeneration);
    }
}
