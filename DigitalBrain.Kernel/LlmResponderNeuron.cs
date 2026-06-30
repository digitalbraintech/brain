using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.llm-responder")]
public class LlmResponderNeuron : Neuron, ILlmResponderNeuron
{
    // Cache scoped clients per (provider, key) so a chatty pack does not rebuild a client per message.
    private readonly Dictionary<(string Provider, string? Key), IChatClient> _scopedClients = new();

    public LlmResponderNeuron(ILogger<LlmResponderNeuron> logger, NeuronJournals journals)
        : base(logger, journals) { }

    public async Task HandleAsync(AskLlm ask)
    {
        var chat = await ResolveChatClientAsync(ask);
        var text = chat is null ? "[no-llm]" : (await chat.GetResponseAsync(ask.Prompt)).Text?.Trim() ?? "[no-llm]";
        var props = new Dictionary<string, object?>(ask.ReplyProps) { ["text"] = text };
        await Broadcast(new Signal(ask.ReplyType, props));
    }

    private async Task<IChatClient?> ResolveChatClientAsync(AskLlm ask)
    {
        if (ask.ConfigPack is null)
            return ServiceProvider.GetService<IChatClient>();

        var factory = ServiceProvider.GetService<IScopedChatClientFactory>();
        var store = ServiceProvider.GetService<IPackConfigStore>();
        if (factory is null || store is null)
            return ServiceProvider.GetService<IChatClient>();

        var values = await store.GetAsync(ask.ConfigScope ?? "default", ask.ConfigPack);
        if (!values.TryGetValue("llm_provider", out var provider) || string.IsNullOrWhiteSpace(provider))
            return ServiceProvider.GetService<IChatClient>();

        values.TryGetValue("llm_key", out var apiKey);
        var key = (provider, string.IsNullOrEmpty(apiKey) ? null : apiKey);
        if (!_scopedClients.TryGetValue(key, out var client))
        {
            client = factory.Create(provider, key.Item2);
            if (client is null)
                return ServiceProvider.GetService<IChatClient>();
            _scopedClients[key] = client;
        }

        return client;
    }
}
