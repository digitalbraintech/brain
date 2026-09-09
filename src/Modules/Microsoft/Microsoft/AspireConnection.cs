using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Sdk;
using DigitalBrain.Product.Interactions;
using ModelContextProtocol.Client;

namespace DigitalBrain.Microsoft;

public sealed class AspireConnection : McpAgentTools
{
    internal AspireConnection(AspireConnectionSettings settings, IUntrustedContentScreen screen)
        : base(new McpStdioConnection
        {
            Name = "aspire",
            Command = settings.Command,
            Arguments = ["agent", "mcp", "--non-interactive", "--log-level", "Error"],
            WorkingDirectory = Path.GetDirectoryName(settings.ProjectPath),
            AllowedToolNames = ["list_resources", "list_console_logs", "list_structured_logs", "list_traces", "list_trace_structured_logs"],
            OperationTimeout = TimeSpan.FromSeconds(30),
            ResponseBudgetBytes = 128 * 1024,
        }, screen, (client, agent, cancellationToken) => BindApplicationAsync(client, agent, settings, cancellationToken))
        => ApplicationName = settings.ApplicationName;

    public string ApplicationName { get; }

    internal static async Task BindApplicationAsync(
        McpClient client, NeuronId agent, AspireConnectionSettings settings, CancellationToken cancellationToken)
    {
        if (agent.Owner != settings.Owner
            || !PrincipalPartition.TryParse(agent.Name, out _, out var alias)
            || !string.Equals(alias, settings.Alias, StringComparison.Ordinal))
        {
            throw new McpOperationException("This Aspire connection is not configured for that agent.");
        }

        // The CLI initializes its AppHost catalog lazily. Selecting immediately
        // after MCP initialize reports no AppHosts even when one is running.
        // Explicit discovery establishes the catalog before target selection.
        var discovered = await client.CallToolAsync("list_apphosts", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (discovered.IsError == true)
        {
            throw new McpOperationException("Aspire could not discover its running applications.");
        }

        // Operational tools remain discovered functions with server-owned schemas.
        var result = await client.CallToolAsync("select_apphost",
            new Dictionary<string, object?> { ["appHostPath"] = settings.ProjectPath },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.IsError == true)
        {
            throw new McpOperationException("The configured Aspire application is unavailable. Start its AppHost and try again.");
        }
    }
}
