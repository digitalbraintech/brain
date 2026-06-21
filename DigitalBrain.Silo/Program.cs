using DigitalBrain.Protocol;
using Orleans;
using Orleans.Journaling;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyedRedisClient("redis");
builder.AddOllamaApiClient("qwen");

builder.UseOrleans(siloBuilder =>
{
    // Orleans Journalling alpha (DurableGrain + IDurable* collections for synapse timelines)
    // Use in-memory list for prototype (no Redis journal storage provider in current alpha; replace with proper provider for durable across restarts)
    siloBuilder.ConfigureServices(services =>
    {
        services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("journal", (_, _) => new DigitalBrain.Silo.InMemoryDurableList<DigitalBrain.Protocol.Synapse>());
        services.AddSingleton<Orleans.Journaling.IJournaledStateManager, DigitalBrain.Silo.TestJournaledStateManager>();
    });
});

var host = builder.Build();

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
var grainFactory = host.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    var status = grainFactory.GetGrain<ISystemStatus>("status-main");
    // Touch to activate (it fires SystemLaunched + status in OnActivate)
    _ = status.GetTimelineAsync();
}

host.Run();
