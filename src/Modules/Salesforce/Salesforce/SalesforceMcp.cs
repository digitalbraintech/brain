using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Salesforce;

// Only provider admission and account policy live here; SDK owns transport/catalog/session mechanics.
internal sealed class SalesforceMcp : IAsyncDisposable
{
    internal static readonly string[] AllowedTools = ["getUserInfo", "soqlQuery", "createRecord", "updateRecord"];
    private readonly SalesforceConnections _connections;
    private readonly McpDiscoveredToolClient<SalesforceInvocation> _client;

    internal SalesforceMcp(McpEndpoint endpoint, SalesforceConnections connections)
    {
        _connections = connections;
        _client = McpDiscoveredToolClient<SalesforceInvocation>.ForHttp(endpoint, connections,
            static identity => identity.Agent.Owner, Authorize, AllowedTools,
            new McpToolPolicy(static name => SalesforceLogins.ReadTools.Contains(name, StringComparer.Ordinal)),
            new McpSessionOptions { ResponseBudgetBytes = 128 * 1024 });
    }

    internal SalesforceMcp(SalesforceConnections connections, McpDiscoveredToolClient<SalesforceInvocation> client)
        => (_connections, _client) = (connections, client);

    internal SalesforceInvocation Identity(AgentToolContext context)
    {
        context.RequireActive();
        if (context.Principal is not { } principal || VerifiedActor.Current?.PrincipalId != principal
            || context.Agent.Type != "salesforce" || !PrincipalPartition.OwnsInstance(principal, context.Agent.Name))
        {
            throw new McpOperationException("The Salesforce specialist requires its authenticated principal.", McpFailureKind.AccessDenied);
        }
        var identity = new SalesforceInvocation(context.Agent, principal, _connections.Identity(context.Owner, principal));
        Authorize(identity, identity.Binding);
        return identity;
    }

    internal void Authorize(SalesforceInvocation identity, SalesforceBinding binding)
    {
        if (identity.Binding != binding || identity.Principal != binding.Principal
            || identity.Agent.Owner != binding.Owner || identity.Agent.Type != "salesforce"
            || VerifiedActor.Current?.PrincipalId != identity.Principal
            || !PrincipalPartition.OwnsInstance(identity.Principal, identity.Agent.Name)
            || _connections.Identity(identity.Agent.Owner, identity.Principal) != binding)
        {
            throw new McpOperationException("The Salesforce account binding changed or is unavailable to this principal.", McpFailureKind.ConnectionChanged);
        }
        var turn = AgentTurnContext.Current;
        if (turn is not null && (turn.Actor.PrincipalId != identity.Principal || turn.Chat.Owner != identity.Agent.Owner))
        {
            throw new McpOperationException("The Salesforce operation does not belong to this authenticated chat.", McpFailureKind.AccessDenied);
        }
        if (turn?.AllowedToolNames is { } allowed)
        {
            var continuation = turn.SpecialistContinuation;
            if (continuation is null || continuation.Target != identity.Agent
                || continuation.ConnectionRevision != binding.Revision
                || allowed.Length == 0 || allowed.Any(name => !SalesforceLogins.ReadTools.Contains(name, StringComparer.Ordinal))
                || !allowed.Order(StringComparer.Ordinal).SequenceEqual(continuation.AllowedToolNames.Order(StringComparer.Ordinal)))
            {
                throw new McpOperationException("This Salesforce continuation no longer matches its exact account and read scope.", McpFailureKind.AccessDenied);
            }
        }
    }

    internal Task<IReadOnlyList<AIFunction>> GetToolsAsync(SalesforceInvocation identity, CancellationToken cancellationToken)
        => _client.GetToolsAsync(identity, cancellationToken);

    internal async Task<object?> SubmitAsync(SalesforceInvocation identity, string toolName, string schema,
        string? resultSchema, JsonElement arguments, CancellationToken cancellationToken)
    {
        Authorize(identity, identity.Binding);
        if (AgentTurnContext.Current?.AllowedToolNames is not null || toolName is not ("createRecord" or "updateRecord"))
        {
            throw new McpOperationException("Only a fresh user confirmation may submit this Salesforce change.");
        }
        await _client.InvalidateAsync(identity, cancellationToken).ConfigureAwait(false);
        var tools = await _client.GetToolsAsync(identity, cancellationToken).ConfigureAwait(false);
        var tool = tools.SingleOrDefault(tool => tool.Name == toolName);
        if (tool is null || tool.JsonSchema.GetRawText() != schema || tool.ReturnJsonSchema?.GetRawText() != resultSchema)
        {
            throw new McpOperationException("The Salesforce tool schema changed. Request and confirm a fresh preview.", McpFailureKind.CatalogChanged);
        }
        var values = arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        return await tool.InvokeAsync(new AIFunctionArguments(values), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

internal sealed record SalesforceInvocation(NeuronId Agent, PrincipalId Principal, SalesforceBinding Binding);
