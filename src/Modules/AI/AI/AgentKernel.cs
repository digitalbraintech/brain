using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;

namespace DigitalBrain.AI;

[GrainType(IAgentKernel.GrainTypeName)]
[StatelessWorker(1)]
[Reentrant]
internal sealed class AgentKernel(IChatClient chatClient, IGrainFactory grains, IServiceProvider services)
    : Grain, IAgentKernel
{
    public async Task<AgentReply> Ask(
        AgentRequest request,
        CorrelationId conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var text = new StringBuilder();
        await foreach (var chunk in AskStreaming(
            [new ChatMessage(ChatRole.User, request.Text)],
            conversationId,
            cancellationToken).ConfigureAwait(true))
        {
            text.Append(chunk.Text);
        }

        return new AgentReply(text.ToString());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> AskStreaming(
        IReadOnlyList<ChatMessage> messages,
        CorrelationId conversationId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        var assistant = NeuronId.FromGrainKey("assistant", this.GetPrimaryKeyString());
        var turn = grains.GetGrain<IAssistantTurn>(assistant.ToGrainId());
        using var activity = AgentTelemetry.Start(assistant, "Ino",
            chatClient.GetService<OpenTelemetryChatClient>()?.EnableSensitiveData is true);
        using var requests = new KernelTurnRequests(turn, assistant, conversationId, cancellationToken);
        using var context = new AgentToolContext(assistant, VerifiedActor.Current?.PrincipalId, requests,
            observation => turn.RecordTurnFact(observation, conversationId, cancellationToken));
        var operation = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        var state = "cancelled";
        await turn.RecordTurnFact(new AgentActivity(operation, "agent", "started", "Ino"), conversationId, cancellationToken)
            .ConfigureAwait(true);
        try
        {
            var tools = new List<AITool>();
            foreach (var source in services.GetServices<IAgentToolSource>())
            {
                tools.AddRange(await source.GetToolsAsync(context, cancellationToken).ConfigureAwait(true));
            }

            if (AgentTurnContext.Current?.AllowedToolNames is { } allowedToolNames)
            {
                var allowed = new HashSet<string>(allowedToolNames, StringComparer.Ordinal);
                tools = [.. tools.Where(tool => allowed.Contains(tool.Name))];
            }

            var options = new ChatOptions { MaxOutputTokens = 4096 };
            if (tools.Count > 0)
            {
                var turnScheduler = TaskScheduler.Current;
                options.Tools = [.. tools.Select(tool =>
                    tool is AIFunction capability
                        ? new TurnBoundFunction(capability, turnScheduler)
                        : tool)];
            }

            IReadOnlyList<ChatMessage> request =
            [
                new ChatMessage(ChatRole.System, DigitalBrain.Assistant.Assistant.InstructionsText),
                .. messages
            ];
            await using var stream = chatClient.GetStreamingResponseAsync(request, options, cancellationToken)
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
                if (activity is not null)
                {
                    Activity.Current = activity;
                }
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

            await turn.RecordTurnFact(
                    new AgentActivity(operation, "agent", state, "Ino",
                        DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds),
                    conversationId,
                    cancellationToken)
                .ConfigureAwait(true);
        }
    }

    private sealed class KernelTurnRequests(
        IAssistantTurn turn,
        NeuronId assistant,
        CorrelationId conversationId,
        CancellationToken turnCancellation) : IAgentRequests, IDisposable
    {
        private bool _active = true;

        public async Task<DeliveryOutcome> SendAsync(NeuronId target, Signal signal, CancellationToken cancellationToken = default)
        {
            RequireTarget(target);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turnCancellation, cancellationToken);
            return await turn.SendFact(target, signal, conversationId, deadline.Token).ConfigureAwait(true);
        }

        public Task<TResponse> RequestAsync<TResponse>(NeuronId target, Signal<TResponse> request, CancellationToken cancellationToken = default)
            where TResponse : Signal
            => throw new NotSupportedException("Kernel turns request specialists through AgentRequest.");

        public async Task<AgentReply> RequestAsync<TAgent>(
            string instanceName, AgentRequest request, CancellationToken cancellationToken = default)
            where TAgent : IAgent
        {
            if (!_active)
            {
                throw new InvalidOperationException("This agent request capability has expired with its model turn.");
            }

            ArgumentNullException.ThrowIfNull(request);
            if (VerifiedActor.Current is not { } actor || !PrincipalPartition.OwnsInstance(actor.PrincipalId, instanceName))
            {
                throw new NeuronAuthorizationException("The delegated agent must belong to the current user.");
            }

            var target = NeuronId.For<TAgent>(assistant.Owner, instanceName);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turnCancellation, cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            return await turn.RequestSpecialist(target, request, conversationId, deadline.Token).ConfigureAwait(true);
        }

        private void RequireTarget(NeuronId target)
        {
            if (!_active || target.Owner != assistant.Owner || VerifiedActor.Current is not { } actor
                || !PrincipalPartition.OwnsInstance(actor.PrincipalId, target.Name))
            {
                throw new NeuronAuthorizationException("Composition requires an active turn and a neuron owned by the current user.");
            }
        }

        public void Dispose() => _active = false;
    }
}
