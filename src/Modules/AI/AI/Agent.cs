using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

// LLM turn + optional discovered MCP tools. Delegation uses this neuron's send
// path; it never re-enters IDigitalBrain's serialized owner root.
public abstract partial class Agent : Neuron, IAgent, IAgentKernel
{
    private readonly IChatClient _chatClient;

    protected Agent(NeuronRuntime runtime, IChatClient chatClient)
        : base(runtime)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        _chatClient = chatClient;
        _completedRequests = ServiceProvider.GetRequiredKeyedService<Orleans.Journaling.IDurableDictionary<string, AgentRequestResult>>("agent.completed-requests");
    }

    protected abstract string Instructions { get; }

    protected virtual string DisplayName => Id.Type;

    protected virtual ValueTask<IReadOnlyList<AITool>> PrepareToolsAsync(
        AgentToolContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<AITool>>([]);

    public async Task HandleAsync(AgentRequest signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var reply = await AskDurablyAsync(signal, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReplyAsync(reply).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task<AgentReply> Ask(AgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var text = new StringBuilder();
        await foreach (var chunk in AskStreaming(
            [new ChatMessage(ChatRole.User, request.Text)],
            cancellationToken).ConfigureAwait(true))
        {
            text.Append(chunk.Text);
        }

        return new AgentReply(text.ToString());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> AskStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = AgentTelemetry.Start(Id, DisplayName,
            _chatClient.GetService<OpenTelemetryChatClient>()?.EnableSensitiveData is true);
        using var requests = new TurnRequests(this, cancellationToken);
        using var context = new AgentToolContext(Id, VerifiedActor.Current?.PrincipalId, requests,
            async observation => { await RecordOutgoingAsync(observation).ConfigureAwait(true); });
        var operation = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        var state = "cancelled";
        await RecordOutgoingAsync(new AgentActivity(operation, "agent", "started", DisplayName))
            .ConfigureAwait(true);
        try
        {
            IReadOnlyList<AITool> tools;
            try
            {
                tools = await PrepareToolsAsync(context, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                state = error is OperationCanceledException ? "cancelled" : "failed";
                throw;
            }
            if (AgentTurnContext.Current?.AllowedToolNames is { } allowedToolNames)
            {
                var allowed = new HashSet<string>(allowedToolNames, StringComparer.Ordinal);
                // Apply the trusted continuation allowlist to every tool type, including
                // server-side tools. OAuth consent must never enable an automatic write.
                tools = [.. tools.Where(tool => allowed.Contains(tool.Name))];
            }
            var options = new ChatOptions { MaxOutputTokens = 4096 };
            if (tools.Count > 0)
            {
                var turnScheduler = TaskScheduler.Current;
                options.Tools = [.. tools.Select(tool =>
                tool is AIFunction capability
                    ? new TurnBoundFunction(capability, turnScheduler) : tool)];
            }
            IReadOnlyList<ChatMessage> request = string.IsNullOrWhiteSpace(Instructions)
                ? messages
                : [new ChatMessage(ChatRole.System, Instructions), .. messages];

            await using var stream = _chatClient.GetStreamingResponseAsync(request, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await stream.MoveNextAsync().ConfigureAwait(true);
                }
                catch (Exception error)
                {
                    state = error is OperationCanceledException ? "cancelled" : "failed";
                    throw;
                }

                if (!hasNext)
                {
                    state = "completed";
                    break;
                }

                yield return stream.Current;
                // An async stream consumer can change the ambient activity between
                // chunks. Keep subsequent model/tool iterations under this agent.
                if (activity is not null) { Activity.Current = activity; }
            }
        }
        finally
        {
            activity?.SetTag("db.agent.state", state);
            if (state == "failed")
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("error.type", "agent_error");
            }
            await RecordOutgoingAsync(new AgentActivity(operation, "agent", state, DisplayName,
                DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds))
                .ConfigureAwait(true);
        }
    }

}
