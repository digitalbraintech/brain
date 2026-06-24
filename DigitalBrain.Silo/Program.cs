using Azure.Storage.Blobs;
using DigitalBrain.Protocol;
using DigitalBrain.Silo;
using DigitalBrain.Silo.Foundry;
using DigitalBrain.Silo.Llm;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Journaling.Json;

// Prototype silo host for DigitalBrain.
// Aspire-hosted path: env vars ConnectionStrings__clustering / grainstate / journal are injected by Aspire.
// Fast path (samples/QuickTest -- kernel): none of those env vars present → localhost clustering + in-memory journals.

#pragma warning disable ORLEANSEXP005

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    // gRPC gateway (prior-knowledge HTTP/2, no TLS).
    options.ListenAnyIP(8080, listen => listen.Protocols = HttpProtocols.Http2);
    // Co-hosted MCP server. Streamable-HTTP/SSE needs HTTP/1.1, so it gets its own endpoint (internal-only).
    options.ListenAnyIP(8081, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.AddGrpc();

// Co-host the MCP tool surface in-process: the tools resolve grains via the silo's own IGrainFactory,
// eliminating the cross-process Orleans-client hop the standalone stdio server incurs. Internal-only — no
// External ingress is wired (remote exposure awaits an auth decision before mutation tools go outside).
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrain.Mcp.Tools.DigitalBrainTools>();
builder.Services.AddSingleton<DigitalBrain.Mcp.Tools.DigitalBrainTools>();

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
builder.Services.AddKernelSecurity(builder.Configuration);
builder.Services.AddEconomics(builder.Configuration);

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

var app = builder.Build();

app.MapGrpcService<DigitalBrain.Silo.Gateway.GatewayService>();

// MCP endpoints answer only on the HTTP/1.1-capable port (8081); gRPC keeps 8080 to itself.
app.MapMcp().RequireHost("*:8081");

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
var grainFactory = app.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    var status = grainFactory.GetGrain<ISystemStatus>("status-main");
    _ = status.GetTimelineAsync();
    _ = grainFactory.GetGrain<IInoCodeEditor>("ino-editor-main").GetTimelineAsync();
    _ = grainFactory.GetGrain<IContextNeuron>("context-main").GetTimelineAsync();
    _ = grainFactory.GetGrain<IDbSupportNeuron>("db-main").GetTimelineAsync();
    _ = grainFactory.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();
}

app.Run();

public partial class Program;
