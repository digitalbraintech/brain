namespace DigitalBrain.Sdk;

/// <summary>A configured MCP connection. Tool permission is independent of catalog discovery.</summary>
public sealed record McpStdioConnection
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyCollection<string> AllowedToolNames { get; init; } = [];
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public int Capacity { get; init; } = 32;
    public int ResponseBudgetBytes { get; init; } = 1_048_576;
}
