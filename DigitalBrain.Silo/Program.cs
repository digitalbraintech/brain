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
    // WARNING: Current journal is prototype-only (in-memory).
    // The DurableGrain alpha + IDurableList is used for synapse timelines.
    // Real deployments require a durable journal + IJournaledStateManager.
    siloBuilder.ConfigureServices(services =>
    {
        // Minimal prototype journal (local to this host, lost on restart)
        services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("journal",
            (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());

        // Stub for the alpha journaling manager (sufficient for local dev / test cluster)
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

// ---- Prototype-only journal support (delete/replace for production) ----
#pragma warning disable ORLEANSEXP005
internal sealed class InMemoryJournalForPrototype<T> : List<T>, Orleans.Journaling.IDurableList<T>;
internal sealed class PrototypeJournaledStateManager : Orleans.Journaling.IJournaledStateManager
{
    public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public void RegisterState(string stateId, Orleans.Journaling.IJournaledState state) { }
    public bool TryGetState(string stateId, out Orleans.Journaling.IJournaledState? state) { state = null; return false; }
    public ValueTask WriteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DeleteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
#pragma warning restore ORLEANSEXP005
