
namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.context-slot.v1")]
public sealed record ContextSlot(
    [property: Id(0)] ContextPath Path,
    [property: Id(1)] ContextEntry Entry);

[GenerateSerializer]
[Alias("db.execution-context-state.v1")]
public sealed record ExecutionContextState(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] IReadOnlyList<ContextSlot> Slots);
