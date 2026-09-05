using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Abstractions.Journals;

[GenerateSerializer]
[Alias("db.journal-read")]
public sealed record JournalRead(
    [property: Id(0)] long ResumeSequence,
    [property: Id(1)] IReadOnlyList<SignalDelivery> Delta,
    [property: Id(2)] JournalSnapshot? ResetSnapshot,
    [property: Id(3)] IReadOnlyList<UnknownJournalEntry>? UnknownEntries = null)
{
    // Delta excludes entries whose historical signal contract is unavailable. Their
    // sequence positions still count when resuming or identifying known deliveries.
    public long SequenceOf(int deltaIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(deltaIndex, Delta.Count);

        var sequence = ResumeSequence - Delta.Count - (UnknownEntries?.Count ?? 0) + deltaIndex + 1;
        if (UnknownEntries is not null)
        {
            foreach (var entry in UnknownEntries.OrderBy(entry => entry.Sequence))
            {
                if (entry.Sequence <= sequence)
                {
                    sequence++;
                }
            }
        }

        return sequence;
    }
}
