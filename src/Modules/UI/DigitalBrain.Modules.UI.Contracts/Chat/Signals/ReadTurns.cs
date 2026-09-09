using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.read-turns")]
public sealed record ReadTurns : Signal<TurnsRead>;

[GenerateSerializer]
[Alias("chat.turns-read")]
public sealed record TurnsRead(
    [property: Id(0)] IReadOnlyList<ChatTurnSnapshot> Turns) : Signal;
