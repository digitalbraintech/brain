using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// In-silo return-value surface. Owners and scripts use IAgent + RequestAsync.
// ChatTurnWorker uses streaming for the conversational responder; specialist requests
// use their initiating neuron's source-bound signal delivery.
[Alias("agent.runtime")]
public interface IAgentKernel : IGrainWithStringKey
{
    [Alias(nameof(Ask))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<AgentReply> Ask(
        AgentRequest request,
        CorrelationId conversationId,
        CancellationToken cancellationToken = default);

    [Alias(nameof(AskStreaming))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    IAsyncEnumerable<ChatResponseUpdate> AskStreaming(
        IReadOnlyList<ChatMessage> messages,
        CorrelationId conversationId,
        CancellationToken cancellationToken = default);

}
