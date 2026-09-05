using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

[GenerateSerializer]
internal sealed record DurableTurnRecord(
    [property: Id(0)] Guid TurnId,
    [property: Id(1)] Guid CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ActorContext Actor,
    [property: Id(4)] ChatTurnStatus Status,
    [property: Id(5)] long Revision,
    [property: Id(6)] UserActionRequest? UserAction = null,
    [property: Id(7)] string[]? AllowedToolNames = null,
    [property: Id(8)] string? Answer = null,
    [property: Id(9)] string? Detail = null,
    [property: Id(10)] string? CompletedUserActionId = null,
    [property: Id(11)] SpecialistContinuation? SpecialistContinuation = null,
    [property: Id(12)] SetupContinuation? SetupContinuation = null,
    [property: Id(13)] int SetupRecoveryAttempts = 0);
