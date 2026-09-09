namespace DigitalBrain.Abstractions.Journals;

// Historical metadata only: the original encoded entry remains in journal storage.
// It cannot be delivered as a signal and reveals no undecodable payload or identity.
[GenerateSerializer]
[Alias("db.unknown-journal-entry")]
public sealed record UnknownJournalEntry(
    [property: Id(0)] long Sequence,
    [property: Id(1)] int EncodedSize);
