using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.start.v1")]
public sealed record StartExecution(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ExecutionId ExecutionId,
    [property: Id(2)] ChatTurnWorkload Workload,
    [property: Id(3)] IReadOnlyList<ExecutionId>? RelatedExecutions = null) : Signal;
