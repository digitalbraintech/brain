using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace DigitalBrain.Silo.Llm;

public static class DigitalBrainChat
{
    public static IServiceCollection AddDigitalBrainChat(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["DigitalBrain:Llm:Provider"];
        var model = config["DigitalBrain:Llm:Model"] ?? "qwen2.5-coder:1.5b";

        if (string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = config["DigitalBrain:Llm:OllamaEndpoint"] ?? "http://localhost:11434";
            services.AddChatClient(new OllamaApiClient(new Uri(endpoint), model));
        }
        else if (string.Equals(provider, "azureopenai", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = config["DigitalBrain:Llm:AzureOpenAIEndpoint"]
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIEndpoint is required for azureopenai provider.");
            var key = config["DigitalBrain:Llm:AzureOpenAIKey"]
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIKey is required for azureopenai provider.");
            services.AddChatClient(
                new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
                    .GetChatClient(model)
                    .AsIChatClient());
        }
        // No provider → no IChatClient registered; neurons fall back deterministically.

        // Embeddings for the Context neuron's hybrid recall. Registered fail-soft as a NoOp (zero vectors) so RAG
        // is always wired and degrades to keyword scoring; a real Ollama/OpenAI embedding generator is a later phase.
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
        return services;
    }
}
