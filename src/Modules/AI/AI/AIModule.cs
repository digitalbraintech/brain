using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Chat;
using DigitalBrain.AI.WebSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

public sealed class AIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Configuration["DigitalBrain:Mcp:Endpoint"] is { Length: > 0 } mcpEndpoint)
        {
            builder.Services.AddHttpClient("digitalbrain-mcp");
            builder.Services.AddSingleton<IAgentToolSource>(services => new DigitalBrainMcpToolSource(
                new Uri(new Uri(mcpEndpoint), "/mcp"),
                new(builder.Configuration[DigitalBrainNames.Owner] ?? DigitalBrainNames.DefaultOwner),
                () => services.GetRequiredService<IHttpClientFactory>().CreateClient("digitalbrain-mcp"),
                services.GetRequiredService<DigitalBrain.Product.Interactions.IUntrustedContentScreen>()));
        }

        if (!string.IsNullOrWhiteSpace(builder.Configuration["DigitalBrain:Workspace:RepositoryPath"]))
        {
            builder.Services.AddSingleton<IAgentToolSource>(new RepositoryDiffToolSource(builder.Configuration));
        }

        WebSearchHosting.Add(builder.Services, builder.Configuration);

        if (string.Equals(
                builder.Configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            AITestingClients.Add(builder.Services);
            return;
        }

        AIClients.Add(builder.Services);
        AIClients.AddImageGeneration(builder.Services, builder.Configuration);
        VoiceToTextHosting.Add(builder.Services, builder.Configuration);
    }
}
