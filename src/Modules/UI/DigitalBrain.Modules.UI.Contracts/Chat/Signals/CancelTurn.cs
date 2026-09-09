using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.cancel-turn")]
public sealed record CancelTurn(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] TurnId TurnId,
    [property: Id(2)] ActorContext? Actor = null,
    [property: Id(3)] long? ExpectedRevision = null) : Signal;
