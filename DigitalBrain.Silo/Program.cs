using DigitalBrain.Protocol;

// Prototype silo host for DigitalBrain.
// For real marketplace deployment:
//   - Use proper Orleans storage provider (Azure Table / Redis / Cosmos for grain state)
//   - Replace the journal implementation with something durable (not the alpha in-memory stubs)
//   - Package as container image (see upcoming Dockerfile)

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyedRedisClient("redis");
builder.AddOllamaApiClient("qwen");

builder.UseOrleans(siloBuilder =>
{
    // Dual journals (in + out) prototype for kernel.
    siloBuilder.ConfigureServices(services =>
    {
        services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("in-journal",
            (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
        services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("out-journal",
            (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
        services.AddSingleton<Orleans.Journaling.IJournaledStateManager, PrototypeJournaledStateManager>();
    });

    // Enable grain persistence for real marketplace state (published packs, etc.)
    // In Aspire wired deployments this gets upgraded to Redis via the AppHost config.
    // This is critical for the marketplace to survive restarts.
    siloBuilder.AddMemoryGrainStorageAsDefault();

    // Orleans Dashboard (live grains, activations, marketplace view - standalone like MCP)
    // Add via 'dotnet add package Microsoft.Orleans.Dashboard' then uncomment + use .WithOrleansDashboard()
    // siloBuilder.AddDashboard(o => { o.Port = 8080; o.HideTrace = true; });
});

var host = builder.Build();

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
var grainFactory = host.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    var status = grainFactory.GetGrain<ISystemStatus>("status-main");
    _ = status.GetTimelineAsync();
}

host.Run();
