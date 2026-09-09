using System.Text.Json.Serialization;
using DigitalBrain.Chat;
using DigitalBrain.Product.Interactions;
namespace DigitalBrain.Kernel;

internal sealed record ChatTurnEvent(
    long Sequence,
    bool FromUser,
    string Text,
    string CommandId,
    string Signal,
    string NeuronId,
    string Caller,
    string CorrelationId,
    DateTimeOffset Timestamp,
    string? TurnId = null,
    string? Status = null,
    KitCardOffer[]? Cards = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    UserActionRequest? UserAction = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EventId = null);

