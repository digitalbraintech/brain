using Aspire.Hosting.DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

// Support "dotnet run --project NeuroOSPrototype.AppHost brain.example.sc" (or dotnet run brain .sc via launcher).
// .sc declares packed integrations (Telegram.Bot, Flutter as marketplace packs) - no logic inside core, just packed for reuse/distribution.
// Uses IAspireNeuron (fired from start or inside) to start/orchestrate the Aspire project.
// Flutter pack contains/uses Aspire integration (AddFlutterClient extension) to start windows/web.
// 
// Brainstorm use cases (product/tech):
// - Reusable integrations: market install Telegram.Bot; any brain .sc includes it as executable resource (packed bot, no core if).
// - Flutter as pack: install DigitalBrain.UI.AspireFlutter; .sc spins with AddFlutterClient for client + surfaces streaming.
// - Aspire neuron driven: .sc or brain.cs uses IAspireNeuron.StartDistributedApp for full project start from pack config.
// - Distribution: packs from marketplace, .sc for local spin with args (like --token for bot).
// - No logic in core: everything (bot, flutter, custom aspire) is NeuroPack embodied, Aspire resources added declaratively.
// - brain.cs: future C# script using scripting + IAspire to define custom Aspire model for project.
var scriptPath = args.Length > 0 && args[0].EndsWith(".sc", StringComparison.OrdinalIgnoreCase) ? args[0] : null;
bool includeFlutter = true;
bool includeTelegram = false;
if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
{
    var json = File.ReadAllText(scriptPath);
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    var root = doc.RootElement;
    if (root.TryGetProperty("packs", out var packs) && packs.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
        includeFlutter = false;
        includeTelegram = false;
        foreach (var p in packs.EnumerateArray())
        {
            var name = p.GetProperty("name").GetString();
            if (name != null && name.Contains("Flutter", StringComparison.OrdinalIgnoreCase)) includeFlutter = true;
            if (name != null && name.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) includeTelegram = true;
        }
    }
}

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

// Flutter as marketplace pack. When .sc or market includes DigitalBrain.UI.AspireFlutter (or Flutter), use the Aspire integration.
// dotnet run brain .sc spins local with packed integrations (no core logic for specific bots/UIs).
if (includeFlutter)
{
    var flutterUiPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "app"));
    if (Directory.Exists(flutterUiPath))
    {
        // Use the extension from the Flutter pack's Aspire integration for reuse/distribution.
        // ctx.AddFlutterClient("flutter-ui", flutterUiPath, "windows");
        // Fallback direct for now:
        var flutterCommand = builder.Configuration["DigitalBrain:FlutterCommand"]
            ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
            ?? "flutter";

        builder.AddExecutable(
                "flutter-ui",
                flutterCommand,
                flutterUiPath,
                "run",
                "-d",
                "windows")
            .WithReference(ctx.OrleansClient)
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
            .WithEnvironment("DIGITALBRAIN_UI_PACK", "DigitalBrain.UI.AspireFlutter")
            .WithEnvironment("DIGITALBRAIN_UI_TIER1_RESTART_REQUIRED", "true");
    }
}

if (ctx.EnableMcp)
{
    // Expose DigitalBrain MCP (stdio tools) as resource so aspire mcp call can discover registered tools: run_closed_loop, ask_ino, publish_to_marketplace, list_marketplace, etc.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp")
        .WithReference(ctx.OrleansClient)
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm);
}

// Packed Telegram bot as integration (just like user vision): no logic in core, packed in marketplace pack.
// When .sc includes it, add as resource. Real pack would use NeuroPack embodiment or AddExecutable to a bot host.
if (includeTelegram)
{
    // Placeholder for packed integration. In full: the pack provides executable or project.
    builder.AddExecutable(
            "telegram-bot",
            "echo",
            ".",
            "Telegram.Bot pack installed - configure token and run real bot host here. Reusable, no core logic.")
        .WithReference(ctx.OrleansClient);
}

builder.AddProject<Projects.DigitalBrain_Gateway>("gateway")
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
