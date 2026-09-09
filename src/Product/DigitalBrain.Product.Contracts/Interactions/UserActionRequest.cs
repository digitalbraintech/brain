namespace DigitalBrain.Product.Interactions;

// Public control data only. Provider credentials and OAuth state never belong here.
[GenerateSerializer]
[Alias("db.user-action-request")]
public sealed record UserActionRequest(
    [property: Id(0)] string Id,
    [property: Id(1)] string Provider,
    [property: Id(2)] string DisplayName,
    [property: Id(3)] string Message,
    [property: Id(4)] string LoginUrl,
    [property: Id(5)] DateTimeOffset ExpiresAt,
    [property: Id(6)] string[] ResumeToolNames,
    [property: Id(7)] SpecialistContinuation? SpecialistContinuation = null,
    [property: Id(8)] string? Scope = null,
    [property: Id(9)] SetupContinuation? SetupContinuation = null,
    [property: Id(10)] string Stage = "login");
