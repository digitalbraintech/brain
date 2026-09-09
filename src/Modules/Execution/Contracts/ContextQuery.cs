
namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.context-query.v1")]
public sealed record ContextQuery([property: Id(0)] ContextPath Path);
