namespace DigitalBrain.Testing.E2E;

public sealed class BrainE2EOptions
{
    public string[] Args { get; init; } = [];

    // "flutter" mirrors ShellHostingExtensions.DefaultFlutterResourceName (the UI hosting
    // project is not referenced here, so the literal is duplicated rather than linked).
    public string[] ExplicitStart { get; init; } = ["flutter"];

    public string[] ExpectedHealthy { get; init; } = ["kernel", "mcp"];

    // The project resource whose rendered Orleans__* environment is mirrored into the
    // fixture's own Orleans client host. It must be client-shaped: clustering configured,
    // no silo-only Reminders/GrainStorage sections.
    public string ClientResource { get; init; } = "mcp";

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromMinutes(5);

    // Environment stamped on every project resource (kernel, mcp) before boot, applied by
    // the same ArmProjectResources loop that stamps the Testing mode.
    public Dictionary<string, string> ProjectEnvironment { get; init; } = new(StringComparer.Ordinal);
}
