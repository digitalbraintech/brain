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
public abstract partial class Agent : Neuron, IAgent
{
    protected Agent(NeuronRuntime runtime, IChatClient chatClient)
        : base(runtime)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
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
        var reply = await HandleApplicationAsync(signal, cancellationToken).ConfigureAwait(true)
            ?? await AskDurablyAsync(signal, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReplyAsync(reply).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    protected virtual Task<AgentReply?> HandleApplicationAsync(
        AgentRequest signal, CancellationToken cancellationToken)
        => Task.FromResult<AgentReply?>(null);

    private IAgentKernel Kernel
        => GrainFactory.GetGrain<IAgentKernel>(IAgentKernel.IdFor(Id.Owner));

    protected Task<AgentReply> Ask(
        AgentRequest request,
        CorrelationId conversationId,
        CancellationToken cancellationToken = default)
        => Kernel.Ask(request, conversationId, cancellationToken);

}
