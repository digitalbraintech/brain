namespace DigitalBrain.Sdk;

/// <summary>Safe operational identity; exception details from a remote server are never required.</summary>
public enum McpFailureKind
{
    Unavailable,
    CatalogChanged,
    ConnectionChanged,
    AccessDenied,
    ContentRejected,
    Capacity,
}
