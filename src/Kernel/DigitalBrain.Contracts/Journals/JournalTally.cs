namespace DigitalBrain.Abstractions.Journals;

[GenerateSerializer]
[Alias("db.journal-tally")]
public sealed record JournalTally([property: Id(0)] string SignalType, [property: Id(1)] long Recorded);
