namespace DigitalBrain.Sdk;

// Provider identity for one browser login rail: the UserActionRequest fields the UI shows, the
// kernel paths the provider's OAuth client must have registered, and the authentication scheme
// the login path challenges.
public sealed record BrowserLoginDefinition(
    string Provider,
    string DisplayName,
    string Scheme,
    string LoginPath,
    string CallbackPath,
    string Message)
{
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(10);

    public int Capacity { get; init; } = 128;

    // Opt in only for bounded, idempotent setup requests whose public scope can be
    // kept with the durable chat intent. OAuth state and credentials stay transient.
    public bool RecoverPendingAfterRestart { get; init; }
    public TimeSpan ReadinessCheckInterval { get; init; } = TimeSpan.FromSeconds(5);
}
