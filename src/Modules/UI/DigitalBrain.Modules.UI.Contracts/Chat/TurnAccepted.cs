using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn-accepted")]
public sealed record TurnAccepted(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] ChatTurnStatus Status) : Signal;
