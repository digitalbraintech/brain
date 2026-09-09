using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.AI;

// Shared execution evidence. MCP business content has no corresponding domain contract.
[GenerateSerializer]
[Alias("db.agent-activity")]
public sealed record AgentActivity(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string State,
    [property: Id(3)] string Name,
    [property: Id(4)] NeuronId? Target = null,
    [property: Id(5)] string? Server = null,
    [property: Id(6)] double? DurationMs = null,
    [property: Id(7)] string? Preview = null,
    [property: Id(8)] bool IsError = false,
    [property: Id(9)] bool Truncated = false,
    [property: Id(10)] string? FailureCode = null) : Signal;
