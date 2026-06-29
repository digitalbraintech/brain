using System.IO;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Company;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.Ui;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Journaling.Json;

// Prototype silo host for DigitalBrain.
// Aspire-hosted path: env vars ConnectionStrings__clustering / grainstate / journal are injected by Aspire.
// Fast path (samples/QuickTest -- kernel): none of those env vars present → localhost clustering + in-memory journals.

#pragma warning disable ORLEANSEXP005

var builder = WebApplication.CreateBuilder(args);
var isAspireHosted = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__clustering"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__grainstate"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__journal"));

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    if (isAspireHosted)
    {
        var grpcPorts = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var grpcPort in grpcPorts)
        {
            if (int.TryParse(grpcPort, out var grpcEndpointPort))
            {
                options.ListenAnyIP(grpcEndpointPort, listen => listen.Protocols = HttpProtocols.Http2);
            }
        }

        var webPort = Environment.GetEnvironmentVariable("DIGITALBRAIN_WEB_PORT");
        if (int.TryParse(webPort, out var webEndpointPort))
        {
            options.ListenAnyIP(webEndpointPort, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
        }
        return;
    }

    options.ListenAnyIP(8080, listen => listen.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(8081, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.AddGrpc();

var corsOrigins = builder.Configuration
    .GetSection("DigitalBrain:Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://digitalbrain.tech" };

builder.Services.AddCors(options => options.AddPolicy("browser", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));

// Server-driven UI fanout: neurons broadcast RfwCards; WatchHomeFeed gRPC subscribers stream them.
// The per-silo HomeFeedStreamSubscriber (wired into the silo below) re-fans cards from the shared Orleans
// MemoryStream so cards broadcast on any silo reach all replicas.
builder.Services.AddSingleton<HomeFeedBus>();

// Co-host the MCP tool surface in-process. Only read-only tools are exposed over HTTP (remotely reachable);
// mutation tools are stdio-only (local/trusted) pending a remote auth decision.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<DigitalBrain.Mcp.Tools.DigitalBrainReadTools>();
builder.Services.AddSingleton<DigitalBrain.Mcp.Tools.DigitalBrainReadTools>();

if (isAspireHosted)
{
    // Cloud host (standalone ACA): bind the journal BlobServiceClient from ConnectionStrings__journal here;
    // clustering + grain storage are wired directly in UseOrleans below from their connection strings. (Under an
    // Aspire AppHost those would be wired by WithClustering/WithGrainStorage; in ACA the silo configures Orleans itself.)
    var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
    var grainStorageServiceKey = Environment.GetEnvironmentVariable("Orleans__GrainStorage__Default__ServiceKey") ?? "grainstate";

    builder.AddKeyedAzureTableServiceClient(clusteringServiceKey);
    builder.AddKeyedAzureBlobServiceClient(grainStorageServiceKey);
}

builder.Services.AddDigitalBrainChat(builder.Configuration);
builder.Services.AddKernelSecurity(builder.Configuration, builder.Environment);
builder.Services.AddEconomics(builder.Configuration);
builder.Services.AddContextStore(builder.Configuration);
builder.Services.AddSingleton<ProcessCrystallizer>(sp => new ProcessCrystallizer(sp.GetService<IChatClient>()));
builder.Services.AddSingleton<SkillPackSynthesizer>();

// Proxy to private marketplace (new separate repo) when enabled.
// Register the stub here; real impl uses HttpClient to the marketplace service.
var useRemote = builder.Configuration.GetValue("DigitalBrain:Marketplace:UseRemote", false);
if (useRemote)
{
    builder.Services.AddSingleton<IRemoteMarketplaceClient, DigitalBrain.Kernel.Marketplace.RemoteMarketplaceClientStub>();
}

builder.UseOrleans(siloBuilder =>
{
    siloBuilder.ConfigureServices(services => services.AddScoped<NeuronJournals>());

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
        siloBuilder.AddAzureBlobJournalStorage(options =>
            options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("journal")!))
            .UseJsonJournalFormat(DigitalBrain.Kernel.JournalJsonContext.Default);
    }

    siloBuilder.AddMemoryStreams("HomeFeed");
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    siloBuilder.ConfigureServices(services => services.AddHomeFeedStreamSubscriber());
    siloBuilder.AddFoundry();
});

#pragma warning restore ORLEANSEXP005

var app = builder.Build();

app.UseRouting();
app.UseCors("browser");
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

var webRoot = builder.Configuration["DIGITALBRAIN_WEBROOT"];
var serveWebBundle = !string.IsNullOrWhiteSpace(webRoot) && Directory.Exists(webRoot);
if (serveWebBundle)
{
    var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(webRoot!));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

app.MapGrpcService<DigitalBrain.Kernel.Gateway.GatewayService>();
app.MapGrpcService<DigitalBrain.Kernel.Gateway.UiGatewayService>();

if (!isAspireHosted)
{
    app.MapMcp().RequireHost("*:8081");
}

if (serveWebBundle)
{
    var indexPath = Path.Combine(Path.GetFullPath(webRoot!), "index.html");
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
    });
}

// Bootstrap self-awareness (SystemStatusNeuron will connect MCP + fire Launched on activate)
var grainFactory = app.Services.GetService<IGrainFactory>();
if (grainFactory != null)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var status = grainFactory.GetGrain<ISystemStatus>("status-main");
                await status.GetTimelineAsync();
                await grainFactory.GetGrain<IInoCodeEditor>("ino-editor-main").GetTimelineAsync();
                await grainFactory.GetGrain<IContextNeuron>("context-main").GetTimelineAsync();
                await grainFactory.GetGrain<IDbSupportNeuron>("db-main").GetTimelineAsync();
                await grainFactory.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();
                await grainFactory.GetGrain<IUserSessionNeuron>("session-main").GetTimelineAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Kernel startup neuron warmup failed.");
            }
        });
    });
}

app.Run();

public partial class Program;

