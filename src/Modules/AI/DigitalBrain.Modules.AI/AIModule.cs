using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

public sealed class AIModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(CreateChatClient);
    }

    private static IChatClient CreateChatClient(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var apiKey = configuration["DigitalBrain:AI:ApiKey"]
            ?? configuration["XAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("XAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new UnconfiguredChatClient();
        }

        var endpoint = configuration["DigitalBrain:AI:Endpoint"] ?? "https://api.x.ai/v1";
        var model = configuration["DigitalBrain:AI:Model"] ?? "grok-4.5";
        var client = new OpenAI.OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        return client.GetChatClient(model).AsIChatClient();
    }
}
