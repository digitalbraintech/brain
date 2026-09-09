using DigitalBrain.Product.Interactions;

namespace DigitalBrain.Chat;

// What one awaited turn-worker call returns: the assistant's final answer and its author.
[GenerateSerializer]
[Alias("chat.turn-result")]
public sealed record ChatTurnResult(
    [property: Id(0)] string Answer,
    [property: Id(1)] string Author,
    [property: Id(2)] UserActionRequest? UserAction = null);
