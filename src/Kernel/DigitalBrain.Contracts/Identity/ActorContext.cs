namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.actor-context")]
public sealed record ActorContext(
    [property: Id(0)] PrincipalId PrincipalId,
    [property: Id(1)] string Username);
