namespace DigitalBrain.UI;

[GenerateSerializer]
internal sealed record ChatPublication(
    [property: Id(0)] Guid Id,
    [property: Id(1)] string ContentHash);
