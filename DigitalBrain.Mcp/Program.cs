// DigitalBrain.Mcp - standalone stdio MCP server for DigitalBrain.
// An Orleans CLIENT (like DigitalBrain.Cli) that exposes cluster interactions as MCP tools (DigitalBrain.Mcp.Tools).
// Requires the cluster (silo + redis/table + ollama) to be running — the tools operate on real grains, so there is
// no degraded no-cluster mode (fail-fast). For an in-process, remote-reachable variant the silo co-hosts the same
// tools over HTTP (see DigitalBrain.Silo/Program.cs).

using DigitalBrain.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Orleans client clustering: Aspire injects the provider type. Azure Table in cloud, Redis locally.
var clusteringProvider = Environment.GetEnvironmentVariable("Orleans__Clustering__ProviderType");
if (string.Equals(clusteringProvider, "AzureTableStorage", StringComparison.OrdinalIgnoreCase))
{
    var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
    builder.AddKeyedAzureTableServiceClient(clusteringServiceKey);
}
else
{
    builder.AddKeyedRedisClient("redis");
}

builder.UseOrleansClient();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DigitalBrainTools>();
builder.Services.AddSingleton<DigitalBrainTools>();

var app = builder.Build();

await app.StartAsync();
Console.Error.WriteLine("DigitalBrain MCP server (stdio) started. Ready for tools. Connect via .mcp.json");
await app.WaitForShutdownAsync();
