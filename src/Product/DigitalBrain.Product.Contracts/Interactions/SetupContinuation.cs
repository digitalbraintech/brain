namespace DigitalBrain.Product.Interactions;

// Exact public setup intent recorded before login. Credentials and OAuth state are
// excluded; a resumed tool uses these arguments instead of newly generated ones.
[GenerateSerializer]
[Alias("db.setup-continuation")]
public sealed record SetupContinuation(
    [property: Id(0)] string ToolName,
    [property: Id(1)] string Scope,
    [property: Id(2)] string? BehaviorName = null,
    [property: Id(3)] Guid? BehaviorRevision = null);
