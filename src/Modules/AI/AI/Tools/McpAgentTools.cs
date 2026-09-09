using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DigitalBrain.AI;

public class McpAgentTools(
    McpStdioConnection connection,
    IUntrustedContentScreen screen,
    Func<McpClient, NeuronId, CancellationToken, Task>? configureSession = null) : IAgentToolSource, IAsyncDisposable
{
    private readonly McpDiscoveredToolClient<NeuronId> _client = new(connection, configureSession);

    public string Name => connection.Name;

    public async ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        context.RequireActive();
        if (context.Principal is not { } principal || !PrincipalPartition.OwnsInstance(principal, context.Agent.Name))
        {
            throw new NeuronAuthorizationException("The MCP agent belongs to a different user or has no verified user context.");
        }
        var started = Stopwatch.GetTimestamp();
        var tools = await _client.GetToolsAsync(context.Agent, cancellationToken).ConfigureAwait(true);
        await context.ObserveAsync(new AgentActivity(Guid.NewGuid(), "tool", "completed", "tools/list", Server: Name,
            DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Preview: McpEvidencePreview.Create(string.Join('\n', tools.Select(tool => $"{tool.Name}: {tool.Description}")))))
            .ConfigureAwait(true);
        return tools.Select(tool => (AITool)AgentToolExecution.Observe(context, tool, Name, screen)).ToArray();
    }

    public Task InvalidateAsync(NeuronId agent, CancellationToken cancellationToken = default)
        => _client.InvalidateAsync(agent, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
