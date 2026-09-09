using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Salesforce;

internal sealed class SalesforceTools : IAgentToolSource
{
    private readonly SalesforceMcp? _mcp;
    private readonly SalesforceLogins? _logins;
    private readonly SalesforceWritePreviews? _previews;
    private readonly IUntrustedContentScreen _screen;
    private readonly bool _fake;

    internal SalesforceTools(IUntrustedContentScreen screen, bool fake = false)
        => (_screen, _fake) = (screen, fake);

    internal SalesforceTools(SalesforceMcp mcp, SalesforceLogins logins, SalesforceWritePreviews previews,
        IUntrustedContentScreen screen) => (_mcp, _logins, _previews, _screen) = (mcp, logins, previews, screen);

    public async ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RequireActive();
        if (context.Principal is not { } principal || VerifiedActor.Current?.PrincipalId != principal
            || context.Agent.Type != "salesforce" || !PrincipalPartition.OwnsInstance(principal, context.Agent.Name))
        {
            throw new McpOperationException("Salesforce tools require the authenticated specialist.", McpFailureKind.AccessDenied);
        }
        if (_mcp is null)
        {
            if (_fake)
            {
                return FakeTools(context);
            }
            return Status("Salesforce is not configured. Configure DigitalBrain:Salesforce:Mcp:Endpoint and OAuth privately in Aspire. No external request was made.");
        }
        var operation = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var identity = _mcp.Identity(context);
            var tools = await _mcp.GetToolsAsync(identity, cancellationToken).ConfigureAwait(true);
            if (AgentTurnContext.Current?.AllowedToolNames is { } admitted
                && admitted.Any(name => tools.All(tool => tool.Name != name)))
            {
                throw new McpOperationException("A Salesforce read required by this login continuation is no longer available. Send a fresh request.", McpFailureKind.CatalogChanged);
            }
            await context.ObserveAsync(new AgentActivity(operation, "tool", "completed", "tools/list", Server: "salesforce",
                DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Preview: string.Join(", ", tools.Select(tool => tool.Name)))).ConfigureAwait(true);
            return tools.Select(tool => (AITool)AgentToolExecution.Observe(context,
                new PolicyTool(tool, context, identity, _mcp, _logins!, _previews!), "salesforce", _screen)).ToArray();
        }
        catch (McpAuthenticationRequiredException)
        {
            await context.ObserveAsync(new AgentActivity(operation, "tool", "failed", "tools/list", Server: "salesforce",
                DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds, IsError: true, FailureCode: "authentication_required")).ConfigureAwait(true);
            var action = _logins!.RequireLogin(readOnly: true, cancellationToken);
            return Status($"Salesforce authentication is required. Login action {action.Id} is displayed in the chat. Login can resume reads only; record changes need a fresh preview and confirmation.");
        }
        catch (Exception error)
        {
            await context.ObserveAsync(new AgentActivity(operation, "tool", error is OperationCanceledException ? "cancelled" : "failed",
                "tools/list", Server: "salesforce", DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                IsError: true, FailureCode: error is TimeoutException ? "timeout" : error is OperationCanceledException ? "cancelled" : "unavailable")).ConfigureAwait(true);
            throw;
        }
    }

    private IReadOnlyList<AITool> FakeTools(AgentToolContext context)
    {
        var reads = new[]
        {
            AIFunctionFactory.Create(() => "{\"mode\":\"fake\",\"user\":\"Salesforce fixture\"}", "getUserInfo"),
            AIFunctionFactory.Create((string query) =>
            {
                SalesforceQueryGuard.Validate(query);
                return "{\"mode\":\"fake\",\"records\":[],\"totalSize\":0}";
            }, "soqlQuery"),
        };
        return reads.Select(tool => (AITool)AgentToolExecution.Observe(context, tool, "salesforce-fixture", _screen)).ToArray();
    }

    private static IReadOnlyList<AITool> Status(string text)
        => [AIFunctionFactory.Create(() => text, "salesforce_connection", "Read the configured Salesforce connection state. This does not contact Salesforce.")];

    internal sealed class PolicyTool : DelegatingAIFunction
    {
        private readonly AIFunction _native;
        private readonly AgentToolContext _context;
        private readonly SalesforceInvocation _identity;
        private readonly SalesforceMcp _mcp;
        private readonly SalesforceLogins _logins;
        private readonly SalesforceWritePreviews _previews;

        internal PolicyTool(AIFunction native, AgentToolContext context, SalesforceInvocation identity,
            SalesforceMcp mcp, SalesforceLogins logins, SalesforceWritePreviews previews) : base(native)
            => (_native, _context, _identity, _mcp, _logins, _previews) = (native, context, identity, mcp, logins, previews);

        public override string Description => Name is "createRecord" or "updateRecord"
            ? "Prepare an exact Salesforce change preview only. No record is written until a fresh authenticated user confirms the published preview. " + base.Description
            : base.Description;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            _context.RequireActive();
            var resumed = AgentTurnContext.Current?.AllowedToolNames;
            if (resumed is not null && !resumed.Contains(Name, StringComparer.Ordinal))
            {
                throw new McpOperationException("This Salesforce operation is outside the confirmed read continuation.", McpFailureKind.AccessDenied);
            }
            try
            {
                _mcp.Authorize(_identity, _identity.Binding);
                if (Name is "createRecord" or "updateRecord")
                {
                    await _previews.CreateAsync(_identity, _native, arguments, cancellationToken).ConfigureAwait(true);
                    return JsonSerializer.SerializeToElement(new
                    {
                        status = "preview_ready",
                        message = "The application will display the complete Salesforce change preview.",
                    });
                }
                ValidateRead(Name, arguments);
                return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(true);
            }
            catch (McpAuthenticationRequiredException)
            {
                var action = _logins.RequireLogin(readOnly: Name is "getUserInfo" or "soqlQuery", cancellationToken);
                return JsonSerializer.SerializeToElement(new { status = "authentication_required", actionId = action.Id });
            }
        }
    }

    internal static void ValidateRead(string name, IReadOnlyDictionary<string, object?> arguments)
    {
        if (name == "soqlQuery")
        {
            var value = arguments.GetValueOrDefault("query");
            var query = value is string text ? text : value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : null;
            try { SalesforceQueryGuard.Validate(query ?? throw new McpOperationException("Supply a bounded Salesforce SELECT query.")); }
            catch (ArgumentException)
            {
                throw new McpOperationException("Use one SELECT with an outer WHERE and positive LIMIT. Comments, multiple statements and locking queries are not allowed.");
            }
        }
        else if (name != "getUserInfo")
        {
            throw new McpOperationException("This Salesforce operation is not admitted.");
        }
    }
}
