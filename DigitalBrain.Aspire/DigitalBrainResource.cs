using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a DigitalBrain resource (encapsulates core + marketplace + LLM + TUI setup).
/// MVP: thin model for future extension (replicas, experiences, self-awareness config).
/// </summary>
public sealed class DigitalBrainResource(string name) : Resource(name), IResourceWithConnectionString
{
    // Connection for consumers (e.g. orleans cluster info or future status endpoint)
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"digitalbrain://{Name}");

    public ValueTask<string?> GetConnectionStringAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(ConnectionStringExpression.ValueExpression);
}