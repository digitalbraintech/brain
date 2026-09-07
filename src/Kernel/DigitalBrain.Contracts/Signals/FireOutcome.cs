using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

// What a fire is worth telling the caller: the envelope it minted, and how far it reached.
[GenerateSerializer]
[Alias("db.fire-outcome")]
public sealed record FireOutcome(
    [property: Id(0)] SignalId SignalId,
    [property: Id(1)] CorrelationId CorrelationId,
    [property: Id(2)] int Delivered);
