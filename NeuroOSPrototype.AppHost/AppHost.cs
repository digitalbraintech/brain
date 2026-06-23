using Aspire.Hosting.DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

// Unified with fast start.cs path (memory kernel + surfaces) and full distributed here.
// See framework/start.cs for fast "dotnet run" INO + tasks + marketplace + UiSurfaces (Gmail etc).
// Experiences emit UiSurface (AuthButtonSurface etc) for sdk/flutter_demo + Telegram skeleton.
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.UseLocalMarketplace = true;
})
.WithOrleansDashboard(8080)
.WithMcp();

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo")
    .WithReference(ctx.Orleans)
    .WithReference(ctx.ClusteringTable)
    .WithReference(ctx.GrainBlobs)
    .WithReference(ctx.JournalBlobs)
    .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
    .WithReplicas(1)  // Set to 1 to allow proxy-less orleans-dashboard endpoint; use closed loops for multi-kernel concerns via Aspire
    .WithEndpoint(name: "orleans-dashboard", port: ctx.OrleansDashboardPort ?? 8080, isProxied: false);

var startUi = builder.AddProject<Projects.DigitalBrain_Cli>("start-ui")
    .WithReference(ctx.OrleansClient)
    .WithExplicitStart();

var flutterUiPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "sdk", "flutter_demo"));
if (Directory.Exists(flutterUiPath))
{
    var flutterCommand = builder.Configuration["DigitalBrain:FlutterCommand"]
        ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
        ?? "flutter";

    builder.AddExecutable(
            "flutter-ui",
            flutterCommand,
            flutterUiPath,
            "run",
            "-d",
            "windows",
            "--dart-define",
            "DIGITALBRAIN_SURFACE_TOOL=get_workbench_surfaces",
            "--dart-define",
            "DIGITALBRAIN_ACTION_TOOL=fire_ui_action")
        .WithEnvironment("DIGITALBRAIN_UI_PACK", "DigitalBrain.UI.AspireFlutter")
        .WithEnvironment("DIGITALBRAIN_UI_TIER1_RESTART_REQUIRED", "true");
}

if (ctx.EnableMcp)
{
    // Expose DigitalBrain MCP (stdio tools) as resource so aspire mcp call can discover registered tools: run_closed_loop, ask_ino, publish_to_marketplace, list_marketplace, etc.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp")
        .WithReference(ctx.OrleansClient)
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm);
}

var gateway = builder.AddProject<Projects.DigitalBrain_Gateway>("gateway")
    .WithReference(ctx.OrleansClient)
    .WithReference(ctx.ClusteringTable)
    .WithExternalHttpEndpoints();

silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
silo.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

// Inject Ollama LLM config so AddDigitalBrainChat registers IChatClient in the Aspire-hosted silo.
// Cloud path: override DigitalBrain__Llm__Provider=azureopenai via DIGITALBRAIN_ENV or appsettings.
silo.WithEnvironment("DigitalBrain__Llm__Provider", "ollama");
silo.WithEnvironment("DigitalBrain__Llm__Model", ctx.LlmModel);
silo.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
    ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));
if (ctx.EnableOrleansDashboard)
{
    silo.WithEnvironment("ORLEANS_DASHBOARD_PORT", (ctx.OrleansDashboardPort ?? 8080).ToString());
}

builder.Build().Run();
