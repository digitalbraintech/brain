using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Product.Interactions;
using DigitalBrain.UI;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[JournalProjection]
[Alias("chat.responded")]
public sealed record Responded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text,
    [property: Id(3)] string Author = "",
    [property: Id(4)] KitCardOffer[]? Cards = null,
    [property: Id(5)] UserActionRequest? UserAction = null,
    [property: Id(6)] TurnId? TurnId = null) : Signal;
