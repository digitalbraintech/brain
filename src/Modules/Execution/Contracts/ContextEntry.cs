
namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.context-entry.v1")]
public sealed record ContextEntry(
    [property: Id(0)] string SchemaHash,
    [property: Id(1)] string? PayloadJson,
    [property: Id(2)] string? BlobRef,
    [property: Id(3)] ContextDigest Digest);
