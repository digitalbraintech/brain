using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// The local MCP server currently represents one authenticated owner. Do not map other
// principals onto its credentials. Tools and schemas come from that server, not a second catalog.
public sealed class DigitalBrainMcpToolSource : IAgentToolSource, IAsyncDisposable
{
    private static readonly PrincipalId OwnerPrincipal = new(Guid.Parse("0000dead-0000-0000-0000-000000000001"));
    private readonly OwnerId owner;
    private readonly IUntrustedContentScreen screen;
    private readonly McpDiscoveredToolClient<NeuronId> client;

    public DigitalBrainMcpToolSource(Uri endpoint, OwnerId owner, Func<HttpClient> createHttpClient,
        IUntrustedContentScreen screen)
    {
        this.owner = owner;
        this.screen = screen;
        client = McpDiscoveredToolClient<NeuronId>.ForLocalHttp(new("digitalbrain", endpoint), createHttpClient,
            static name => name != "send_chat_message", new() { Timeout = TimeSpan.FromMinutes(3) });
    }

    public string Name => "digitalbrain";

    public async ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        context.RequireActive();
        if (context.Owner != owner || context.Principal != OwnerPrincipal)
        {
            throw new NeuronAuthorizationException("The local DigitalBrain MCP connection is available only to its configured owner.");
        }
        var tools = await client.GetToolsAsync(context.Agent, cancellationToken).ConfigureAwait(false);
        await context.ObserveAsync(new AgentActivity(Guid.NewGuid(), "tool", "completed", "tools/list", Server: Name,
            Preview: string.Join(", ", tools.Select(tool => tool.Name)))).ConfigureAwait(false);
        return tools.Select(tool => (AITool)AgentToolExecution.Observe(context, tool, Name, screen)).ToArray();
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
