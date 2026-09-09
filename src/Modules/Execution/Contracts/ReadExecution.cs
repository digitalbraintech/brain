using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.read.v1")]
public sealed record ReadExecution : Signal<ExecutionProjection>;
