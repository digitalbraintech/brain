using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Chat;

/// <summary>An application result with a stable publication identity for retry-safe delivery.</summary>
[GenerateSerializer]
[Alias("chat.publish-note")]
public sealed record PublishNote(
    [property: Id(0)] Guid PublicationId,
    [property: Id(1)] string Text) : Signal<NotePublished>;

[GenerateSerializer]
[Alias("chat.note-published")]
public sealed record NotePublished(
    [property: Id(0)] Guid PublicationId,
    [property: Id(1)] bool Duplicate) : Signal;
