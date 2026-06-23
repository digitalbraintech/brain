using Azure.Storage.Blobs;
using DigitalBrain.Protocol;
using DigitalBrain.Silo.Foundry;
using DigitalBrain.Silo.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
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
    // Cloud host (standalone ACA): bind the journal BlobServiceClient from ConnectionStrings__journal here;
    // clustering + grain storage are wired directly in UseOrleans below from their connection strings. (Under an
    // Aspire AppHost those would be wired by WithClustering/WithGrainStorage; in ACA the silo configures Orleans itself.)
    var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
    var grainStorageServiceKey = Environment.GetEnvironmentVariable("Orleans__GrainStorage__Default__ServiceKey") ?? "grainstate";
    const string journalServiceKey = "journal";

    builder.AddKeyedAzureTableServiceClient(clusteringServiceKey);
    builder.AddKeyedAzureBlobServiceClient(grainStorageServiceKey);
    builder.AddKeyedAzureBlobServiceClient(journalServiceKey);

    builder.Services.AddSingleton<IConfigureOptions<AzureBlobJournalStorageOptions>>(sp =>
        new ConfigureNamedOptions<AzureBlobJournalStorageOptions>(
            Options.DefaultName,
            options => options.BlobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>(journalServiceKey)));
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
        // Cloud path: wire Orleans clustering (Table) + grain storage (Blob) from the injected connection strings,
        // then the durable Blob journal. A stable cluster/service id lets the silo rejoin the same cluster on restart.
        var clusterId = builder.Configuration["Orleans:ClusterId"] ?? "digitalbrain";
        var serviceId = builder.Configuration["Orleans:ServiceId"] ?? "digitalbrain";

        siloBuilder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterId;
            options.ServiceId = serviceId;
        });
        siloBuilder.UseAzureStorageClustering(options =>
            options.ConfigureTableServiceClient(builder.Configuration.GetConnectionString("clustering")!));
        siloBuilder.AddAzureBlobGrainStorage("Default", options =>
            options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("grainstate")!));
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
    _ = grainFactory.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();
    // Closed loop activation via Mcp or INO using closed loops only (removed direct INeuron to avoid ambiguity; use mcp and closed loops to activate)
}

host.Run();
