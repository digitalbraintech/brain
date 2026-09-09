using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DigitalBrain.Google;

// Only Gmail policy lives here. The SDK owns HTTP authentication, session leases,
// native schema snapshots, cancellation, response budgets and read-only refresh.
internal sealed class GmailMcp : IAsyncDisposable
{
    internal static readonly string[] NativeTools = ["search_threads", "get_thread", "list_labels", "create_draft"];
    private readonly GmailConnections _connections;
    private readonly IUntrustedContentScreen _screen;
    private readonly McpDiscoveredToolClient<GmailAgentIdentity> _client;

    public GmailMcp(GmailConnections connections, IUntrustedContentScreen screen)
        : this(connections, screen, null) { }

    internal GmailMcp(GmailConnections connections, IUntrustedContentScreen screen,
        McpDiscoveredToolClient<GmailAgentIdentity>? client)
    {
        _connections = connections;
        _screen = screen;
        _client = client ?? McpDiscoveredToolClient<GmailAgentIdentity>.ForHttp(
            new McpEndpoint("gmail", GoogleModule.GmailMcpEndpoint), connections,
            static identity => identity.Agent.Owner, Authorize, NativeTools,
            new McpToolPolicy(static tool => tool != "create_draft", ValidateCatalog),
            new McpSessionOptions { Lifetime = TimeSpan.FromMinutes(10), ResponseBudgetBytes = 1048576, Timeout = TimeSpan.FromSeconds(30) });
    }

    internal Task<IReadOnlyList<AIFunction>> GetToolsAsync(GmailAgentIdentity identity, CancellationToken cancellationToken)
        => _client.GetToolsAsync(identity, cancellationToken);

    internal static void Authorize(GmailAgentIdentity identity, GmailIdentity binding)
    {
        if (VerifiedActor.Current?.PrincipalId != identity.Principal
            || !PrincipalPartition.OwnsInstance(identity.Principal, identity.Agent.Name)
            || identity.Agent.Type != "gmail" || binding.Principal != identity.Principal)
        {
            throw new McpAuthenticationRequiredException();
        }
    }

    internal async Task<JsonElement> InvokeAsync(GmailAgentIdentity identity, AIFunction native,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken, GmailIdentity? draftIdentity = null)
    {
        var binding = _connections.Identity(identity.Agent.Owner, identity.Principal);
        Authorize(identity, binding);
        GmailContent.ValidateArguments(native.Name, arguments);
        if (native.Name == "create_draft" && (draftIdentity is null || draftIdentity != binding || !binding.CanCompose))
        {
            throw new McpOperationException("Draft writes require a consumed trusted preview for the current account.");
        }
        await _screen.ScreenAsync(JsonSerializer.Serialize(arguments), cancellationToken).ConfigureAwait(false);
        if (native.Name == "create_draft")
        {
            // Confirmation must compare the saved native schema against the current catalog,
            // even when the server never sends tools/list_changed. The saved function still
            // carries its original schema and binding; the SDK refuses any mismatch on invoke.
            await _client.InvalidateAsync(identity, cancellationToken).ConfigureAwait(false);
        }
        var raw = await native.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>(arguments)), cancellationToken).ConfigureAwait(false);
        if (raw is not JsonElement envelope || McpDiscoveredTool.IsError(envelope) || McpDiscoveredTool.IsTruncated(envelope))
        {
            throw new McpOperationException("Gmail did not return complete successful evidence. Narrow the request or check service access.");
        }
        var projected = GmailContent.Project(native.Name, ReadContent(envelope), arguments);
        await _screen.ScreenAsync(projected.GetRawText(), cancellationToken).ConfigureAwait(false);
        if (_connections.Identity(identity.Agent.Owner, identity.Principal) != binding)
        {
            throw new McpOperationException("The Gmail connection changed during the request. Start a new request.", McpFailureKind.ConnectionChanged);
        }
        return projected;
    }

    private static JsonElement ReadContent(JsonElement envelope)
    {
        if (envelope.TryGetProperty("structuredContent", out var structured))
        {
            return structured;
        }
        if (envelope.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            var text = string.Join('\n', blocks.EnumerateArray()
                .Where(static block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
                .Select(static block => block.GetProperty("text").GetString()));
            try { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
            catch (JsonException) { }
        }
        throw new McpOperationException("Gmail MCP returned an invalid response shape.");
    }

    private static void ValidateCatalog(IEnumerable<McpClientTool> tools)
    {
        var catalog = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        Check("search_threads", ["query", "pageSize", "pageToken", "includeTrash", "view"], "view", ["THREAD_VIEW_MINIMAL"]);
        Check("get_thread", ["threadId", "messageFormat"], "messageFormat", ["MINIMAL", "PLAIN_TEXT"]);
        Check("list_labels", []);
        Check("create_draft", ["to", "cc", "bcc", "subject", "body"]);
        void Check(string name, string[] fields, string? enumField = null, string[]? values = null)
        {
            if (!catalog.TryGetValue(name, out var tool) || !tool.JsonSchema.TryGetProperty("properties", out var properties)
                || fields.Any(f => !properties.TryGetProperty(f, out _)))
            {
                throw new McpOperationException("The hosted Gmail MCP catalog is incompatible with the supported schema.", McpFailureKind.CatalogChanged);
            }
            if (enumField is not null && (!properties.GetProperty(enumField).TryGetProperty("enum", out var choices)
                || values!.Any(v => !choices.EnumerateArray().Any(c => c.GetString() == v))))
            {
                throw new McpOperationException("The hosted Gmail MCP catalog does not support the required safe content format.", McpFailureKind.CatalogChanged);
            }
            foreach (var field in fields)
            {
                var schema = properties.GetProperty(field);
                var expectedType = field switch { "pageSize" => "integer", "includeTrash" => "boolean", "to" or "cc" or "bcc" => "array", _ => "string" };
                if (!schema.TryGetProperty("type", out var type) || type.GetString() != expectedType)
                {
                    throw new McpOperationException("The hosted Gmail MCP argument types changed; review the provider policy.", McpFailureKind.CatalogChanged);
                }
                if (expectedType == "array" && (!schema.TryGetProperty("items", out var item)
                    || !item.TryGetProperty("type", out var itemType) || itemType.GetString() != "string"))
                {
                    throw new McpOperationException("The hosted Gmail MCP recipient schema is incompatible.", McpFailureKind.CatalogChanged);
                }
            }
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

internal sealed record GmailAgentIdentity(NeuronId Agent, PrincipalId Principal);
