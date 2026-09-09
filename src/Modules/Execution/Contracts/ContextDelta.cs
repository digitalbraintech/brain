
namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.context-delta.v1")]
public sealed record ContextDelta(
    [property: Id(0)] ContextPath Path,
    [property: Id(1)] string SchemaHash,
    [property: Id(2)] string? PayloadJson,
    [property: Id(3)] string? BlobRef);
