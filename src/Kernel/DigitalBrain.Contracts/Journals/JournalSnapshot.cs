namespace DigitalBrain.Abstractions.Journals;

[GenerateSerializer]
[Alias("db.journal-snapshot")]
public sealed record JournalSnapshot(
    [property: Id(0)] long TotalRecorded,
    [property: Id(1)] long LastSequence,
    [property: Id(2)] long EarliestRetainedSequence,
    [property: Id(3)] int RetainedCount,
    [property: Id(4)] IReadOnlyList<JournalTally> Tallies);
