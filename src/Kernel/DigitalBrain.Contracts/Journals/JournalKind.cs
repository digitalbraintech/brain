namespace DigitalBrain.Abstractions.Journals;

[GenerateSerializer]
[Alias("db.journal-kind")]
public enum JournalKind
{
    Incoming,
    Outgoing,
}
