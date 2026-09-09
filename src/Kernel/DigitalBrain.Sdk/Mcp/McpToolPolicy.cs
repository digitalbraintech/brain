using ModelContextProtocol.Client;

namespace DigitalBrain.Sdk;

// Read-only tools get one silent credential refresh and retry after a 401; a write is never
// replayed. ValidateCatalog runs once per session against the server's published tools.
public sealed record McpToolPolicy(
    Func<string, bool> IsReadOnly,
    Action<IEnumerable<McpClientTool>>? ValidateCatalog = null);
