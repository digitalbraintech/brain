using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.workload.chat-turn.v1")]
public sealed record ChatTurnWorkload(
    [property: Id(0)] NeuronId ChatId,
    [property: Id(1)] Guid TurnId,
    [property: Id(2)] string UserText);
