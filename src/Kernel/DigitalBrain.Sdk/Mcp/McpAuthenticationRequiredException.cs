namespace DigitalBrain.Sdk;

public sealed class McpAuthenticationRequiredException : Exception
{
    public McpAuthenticationRequiredException()
        : base("Authorization is required before this MCP server can be used.")
    {
    }

    public McpAuthenticationRequiredException(string message)
        : base(message)
    {
    }

    public McpAuthenticationRequiredException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
