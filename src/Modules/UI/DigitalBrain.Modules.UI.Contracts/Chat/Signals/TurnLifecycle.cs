using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn-lifecycle")]
public sealed record TurnLifecycle(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] NeuronId Chat,
    [property: Id(3)] ChatTurnStatus Status,
    [property: Id(4)] string? Detail = null) : Signal;
