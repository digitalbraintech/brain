using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.lifecycle.v1")]
public sealed record ExecutionLifecycle(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] ExecutionStatus Status,
    [property: Id(2)] string? Detail = null) : Signal;
