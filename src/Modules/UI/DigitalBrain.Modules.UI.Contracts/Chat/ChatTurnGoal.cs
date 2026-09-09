using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Product.Interactions;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.UI;

// Everything the turn worker needs to run one durable chat turn's AI attempt.
[GenerateSerializer]
[Alias("chat.turn-goal")]
public sealed record ChatTurnGoal(
    [property: Id(0)] Guid TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ActorContext Actor,
    [property: Id(4)] NeuronId Chat,
    [property: Id(5)] string[]? AllowedToolNames = null,
    [property: Id(6)] string? CompletedUserActionId = null,
    [property: Id(7)] SpecialistContinuation? SpecialistContinuation = null,
    [property: Id(8)] SetupContinuation? SetupContinuation = null,
    [property: Id(9)] CorrelationId ConversationId = default);
