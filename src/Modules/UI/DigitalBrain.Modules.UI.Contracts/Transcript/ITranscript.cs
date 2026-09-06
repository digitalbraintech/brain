using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.UI;

[Alias("ui.transcript")]
public interface ITranscript : IEntity<TranscriptState>
{
    const string GrainTypeName = "transcript";
    const int DefaultCap = 500;

    [Alias(nameof(Append))]
    Task Append(TranscriptEntry entry, int cap);
}

[GenerateSerializer]
[Alias("ui.transcript-state")]
public sealed record TranscriptState(
    [property: Id(0)] IReadOnlyList<TranscriptEntry> Entries);

[GenerateSerializer]
[Alias("ui.transcript-entry")]
public sealed record TranscriptEntry(
    [property: Id(0)] bool FromUser,
    [property: Id(1)] string Text,
    [property: Id(2)] string CommandId,
    [property: Id(3)] DateTimeOffset At);
