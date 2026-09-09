namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.check-requirement")]
public sealed record GitHubCheckRequirement([property: Id(0)] string Name,
    [property: Id(1)] long? AppId = null, [property: Id(2)] string Kind = "check");
