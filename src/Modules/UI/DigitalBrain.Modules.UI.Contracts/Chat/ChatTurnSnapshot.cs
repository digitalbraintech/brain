using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Product.Interactions;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn-snapshot")]
public sealed record ChatTurnSnapshot(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ChatTurnStatus Status,
    [property: Id(4)] UserActionRequest? UserAction = null,
    [property: Id(5)] string? Answer = null,
    [property: Id(6)] string? Detail = null);
