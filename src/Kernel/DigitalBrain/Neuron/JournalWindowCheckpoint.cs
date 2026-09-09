namespace DigitalBrain.Core;

internal readonly record struct JournalWindowCheckpoint(
    IReadOnlyList<byte[]> Retained,
    IReadOnlyDictionary<string, long> Tallies,
    long LastSequence);
