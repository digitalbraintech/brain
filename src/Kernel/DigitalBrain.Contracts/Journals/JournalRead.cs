using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Journals;

[GenerateSerializer]
[Alias("db.journal-read")]
public sealed record JournalRead(
    [property: Id(0)] long ResumeSequence,
    [property: Id(1)] IReadOnlyList<SignalDelivery> Delta,
    [property: Id(2)] JournalSnapshot? ResetSnapshot);
