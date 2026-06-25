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

var silo = builder.AddProject<Projects.DigitalBrain_Silo>("silo");
ctx.WireKernelSilo(silo);  // Provides kernel cool features out of box (marketplace, surfaces, journals, 3 replicas HA, LLM for built-ins) via the Aspire package.

var startUi = builder.AddProject<Projects.DigitalBrain_Cli>("start-ui")
    .WithReference(ctx.OrleansClient)
    .WithExplicitStart();

var flutterUiPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "sdk", "flutter_demo"));
if (Directory.Exists(flutterUiPath))
{
    var flutterCommand = builder.Configuration["DigitalBrain:FlutterCommand"]
        ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
        ?? "flutter";
    var mcpProjectPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "DigitalBrain.Mcp"));
    var mcpDllPath = Path.Combine(mcpProjectPath, "bin", "Debug", "net11.0", "DigitalBrain.Mcp.dll");

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
        .WithReference(ctx.OrleansClient)
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
        .WithEnvironment("DIGITALBRAIN_UI_PACK", "DigitalBrain.UI.AspireFlutter")
        .WithEnvironment("DIGITALBRAIN_UI_TIER1_RESTART_REQUIRED", "true")
        .WithEnvironment("DIGITALBRAIN_MCP_COMMAND", "dotnet")
        .WithEnvironment("DIGITALBRAIN_MCP_ARGS", mcpDllPath)
        .WithEnvironment("DIGITALBRAIN_MCP_WORKDIR", mcpProjectPath);
}

if (ctx.EnableMcp)
{
    // Expose DigitalBrain MCP (stdio tools) as resource so aspire mcp call can discover registered tools: run_closed_loop, ask_ino, publish_to_marketplace, list_marketplace, etc.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp")
        .WithReference(ctx.OrleansClient)
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm);
}

builder.AddProject<Projects.DigitalBrain_Gateway>("gateway")
    .WithReference(ctx.OrleansClient)
    .WithReference(ctx.ClusteringTable)
    .WithExternalHttpEndpoints();

// E2E support: launch Flutter web-server so browser-driven tests can load the real UI and observe RfwCard / surface rendering
// while packs are installed and embodied. Uses test mode + injected gateway endpoint for gRPC.
var isTestMode = string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_TEST_MODE"), "true", StringComparison.OrdinalIgnoreCase);
if (isTestMode)
{
    var appDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "app"));
    if (Directory.Exists(appDir))
    {
        var flutterCmd = builder.Configuration["DigitalBrain:FlutterCommand"]
            ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
            ?? "flutter";

        // Compute gateway https for the Flutter client (it resolves via KERNEL_ENDPOINT or services__ )
        // We wire it via dart-define so the web app connects back to this test's gateway for WatchHomeFeed.
        var gatewayForFlutter = "https://localhost:8080"; // placeholder - fixture will override via env if needed; real value injected below

        var flutterWeb = builder.AddExecutable(
                "flutter-web",
                flutterCmd,
                appDir,
                "run",
                "-d", "web-server",
                "--web-port", "0",
                "--dart-define", "SURFACE_DEMO=true")
            .WithEnvironment("DIGITALBRAIN_TEST_MODE", "true")
            .WithReference(ctx.OrleansClient);

        // The Flutter web resource provides the live render target. Browser fixture navigates to its endpoint.
        // The client code inside resolves the gateway via Aspire service discovery or dart-define.
    }
}

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
