using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Llm;

// Builds per-scope chat clients. Ollama mirrors DigitalBrainChat (endpoint/model from kernel config);
// OpenAI is constructed from the caller-supplied key. The key is never logged.
public sealed class ScopedChatClientFactory(IConfiguration config) : IScopedChatClientFactory
{
    public IChatClient Create(string provider, string? apiKey)
    {
        var model = config["DigitalBrain:Llm:Model"] ?? "qwen2.5-coder:1.5b";

        if (string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("openai provider requires an API key.");

            var openAiModel = config["DigitalBrain:Llm:OpenAIModel"] ?? "gpt-4o-mini";
            var openAiClient = new OpenAI.Chat.ChatClient(openAiModel, apiKey).AsIChatClient();
            return new ChatClientBuilder(openAiClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
        }

        // Default / "ollama": mirror DigitalBrainChat's Ollama wiring.
        var endpoint = config["DigitalBrain:Llm:OllamaEndpoint"] ?? "http://localhost:11434";
        var ollamaClient = new OllamaSharp.OllamaApiClient(new Uri(endpoint), model);
        return new ChatClientBuilder(ollamaClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
    }
}
