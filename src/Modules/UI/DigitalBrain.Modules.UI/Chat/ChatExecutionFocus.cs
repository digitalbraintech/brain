namespace DigitalBrain.UI;

[GenerateSerializer]
internal sealed record ChatExecutionFocus(
    [property: Id(0)] Guid? ActiveExecutionId,
    [property: Id(1)] List<Guid> RelatedExecutionIds);
