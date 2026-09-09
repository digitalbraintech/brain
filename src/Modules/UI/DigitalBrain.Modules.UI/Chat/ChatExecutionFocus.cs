using DigitalBrain.Execution;

namespace DigitalBrain.UI;

[GenerateSerializer]
internal sealed record ChatExecutionFocus(
    [property: Id(0)] ExecutionId? ActiveExecutionId,
    [property: Id(1)] List<ExecutionId> RelatedExecutionIds);
