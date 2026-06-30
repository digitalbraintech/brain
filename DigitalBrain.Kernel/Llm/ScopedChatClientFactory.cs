using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Llm;

// Builds per-scope chat clients. Ollama mirrors DigitalBrainChat (endpoint/model from kernel config);
// OpenAI is constructed from the caller-supplied key. The key is never logged.
public sealed class ScopedChatClientFactory(IConfiguration config, ILogger<ScopedChatClientFactory> logger) : IScopedChatClientFactory
{
    public IChatClient? Create(string provider, string? apiKey)
    {
        var model = config["DigitalBrain:Llm:Model"] ?? "qwen2.5-coder:1.5b";

        if (string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("openai provider requested but no API key is configured — falling back to global client.");
                return null;
            }

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
