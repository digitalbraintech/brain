using Azure.Storage.Blobs;
using DigitalBrain.Protocol;
using DigitalBrain.Silo.Foundry;
using DigitalBrain.Silo.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.Journaling.Json;

// Prototype silo host for DigitalBrain.
// Aspire-hosted path: env vars ConnectionStrings__clustering / grainstate / journal are injected by Aspire.
// Fast path (samples/QuickTest -- kernel): none of those env vars present → localhost clustering + in-memory journals.

#pragma warning disable ORLEANSEXP005

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var isAspireHosted = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__clustering"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__grainstate"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__journal"));

if (isAspireHosted)
{
    // Aspire injects Azure Storage connection strings via env; register keyed service clients so Orleans + journaling can resolve them.
    builder.AddKeyedAzureTableServiceClient("clustering");
    builder.AddKeyedAzureBlobServiceClient("grainstate");
    builder.AddKeyedAzureBlobServiceClient("journal");

    // Bind keyed journal BlobServiceClient to AzureBlobJournalStorageOptions before UseOrleans sees it.
    builder.Services.AddSingleton<IConfigureOptions<AzureBlobJournalStorageOptions>>(sp =>
        new ConfigureNamedOptions<AzureBlobJournalStorageOptions>(
            Options.DefaultName,
            options => options.BlobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("journal")));
}

builder.Services.AddDigitalBrainChat(builder.Configuration);

builder.UseOrleans(siloBuilder =>
{
    if (!isAspireHosted)
    {
        // Fast path: localhost clustering + in-memory grain storage + in-memory journals.
        siloBuilder.UseLocalhostClustering();
        siloBuilder.AddMemoryGrainStorageAsDefault();
        siloBuilder.ConfigurePrototypeJournals();
    }
    else
    {
        // Aspire path: clustering + grain storage wired by Aspire Orleans integration via WithClustering/WithGrainStorage.
        // Journal: BlobServiceClient bound above via IConfigureOptions; just register the provider + format.
        siloBuilder.AddAzureBlobJournalStorage()
            .UseJsonJournalFormat(DigitalBrain.Protocol.JournalJsonContext.Default);
    }

    siloBuilder.AddFoundry();
});

#pragma warning restore ORLEANSEXP005

var host = builder.Build();

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
var grainFactory = host.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    var status = grainFactory.GetGrain<ISystemStatus>("status-main");
    _ = status.GetTimelineAsync();
    _ = grainFactory.GetGrain<IInoCodeEditor>("ino-editor-main").GetTimelineAsync();
    _ = grainFactory.GetGrain<IContextNeuron>("context-main").GetTimelineAsync();
    _ = grainFactory.GetGrain<IDbSupportNeuron>("db-main").GetTimelineAsync();
    // Closed loop activation via Mcp or INO using closed loops only (removed direct INeuron to avoid ambiguity; use mcp and closed loops to activate)
}

host.Run();
