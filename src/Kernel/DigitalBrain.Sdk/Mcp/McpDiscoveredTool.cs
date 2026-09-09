using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Sdk;

/// <summary>A catalog snapshot whose invocation leases the connection's current session.</summary>
public sealed class McpDiscoveredTool : DelegatingAIFunction
{
    private readonly Func<AIFunctionArguments, CancellationToken, ValueTask<object?>> _invoke;

    internal McpDiscoveredTool(
        string connectionName,
        McpClientTool definition,
        Func<AIFunctionArguments, CancellationToken, ValueTask<object?>> invoke) : base(definition)
    {
        ConnectionName = connectionName;
        ProtocolTool = definition.ProtocolTool;
        _invoke = invoke;
    }

    public string ConnectionName { get; }
    public Tool ProtocolTool { get; }
    public string? Title => ProtocolTool.Title;

    public override object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType == typeof(McpDiscoveredTool)
            ? this
            : base.GetService(serviceType, serviceKey);

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        => _invoke(arguments, cancellationToken);

    public static bool IsError(object? result)
        => result is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True;

    public static bool IsTruncated(object? result)
        => result is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
            meta.TryGetProperty("digitalbrain", out var brain) && brain.ValueKind == JsonValueKind.Object &&
            brain.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True;
}
