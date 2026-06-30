using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.llm-responder")]
public class LlmResponderNeuron : Neuron, ILlmResponderNeuron
{
    public LlmResponderNeuron(ILogger<LlmResponderNeuron> logger, NeuronJournals journals)
        : base(logger, journals) { }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public async Task HandleAsync(AskLlm ask)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        var text = chat is null ? "[no-llm]" : (await chat.GetResponseAsync(ask.Prompt)).Text?.Trim() ?? "[no-llm]";
        var props = new Dictionary<string, object?>(ask.ReplyProps) { ["text"] = text };
        await Broadcast(new Signal(ask.ReplyType, props));
    }
}
