using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Product.Interactions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.complete-user-action")]
public sealed record CompleteUserAction(
    [property: Id(0)] AgentTurnContext Context,
    [property: Id(1)] string ActionId,
    [property: Id(2)] bool Accepted) : Signal;
