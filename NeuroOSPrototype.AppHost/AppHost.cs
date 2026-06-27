using Aspire.Hosting.DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

// brain.cs : thin C# for "dotnet run brain.cs" (setup like dotnet run start.cs).
// Just uses IAspireNeuron to start Aspire project.
// Integrations (Telegram, Flutter) packed as marketplace NeuroPacks - no logic inside brain.cs .
// Pack provides the Aspire bits (see AddFlutterClient).
// Run via the QuickTest setup or equivalent that supports dotnet run brain.cs .

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

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>("kernel");
ctx.WireKernelSilo(kernel);  // Provides kernel cool features out of box (marketplace, surfaces, journals, 3 replicas HA, LLM for built-ins) via the Aspire package.

var startUi = builder.AddProject<Projects.DigitalBrain_Cli>("start-ui")
    .WithReference(ctx.OrleansClient)
    .WithExplicitStart();

// Flutter Windows client — automatically started by `aspire run`.
// The client receives kernel endpoint via Aspire service discovery (services__kernel__http__0 etc.)
// which resolveKernelEndpoint() in the Flutter app already understands.
var flutterUiPath = ResolveFlutterAppPath(builder);
if (!string.IsNullOrEmpty(flutterUiPath))
{
    ctx.AddFlutterClient("flutter-ui", flutterUiPath, "windows")
        .WithReference(kernel);
}
else
{
    Console.WriteLine("[Aspire] WARNING: Could not locate Flutter app directory (app/). Set DIGITALBRAIN_FLUTTER_APP_PATH env var or place the 'app' folder as a sibling of 'brain'. Flutter Windows client will not auto-start.");
}

static string? ResolveFlutterAppPath(IDistributedApplicationBuilder b)
{
    // 1. Explicit override (highest priority)
    var flutterPathEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_FLUTTER_APP_PATH");
    if (!string.IsNullOrWhiteSpace(flutterPathEnv) && System.IO.Directory.Exists(flutterPathEnv))
        return System.IO.Path.GetFullPath(flutterPathEnv);

    // 2. Common relative locations from AppHost
    var appHostDir = b.AppHostDirectory;
    var candidates = new[]
    {
        System.IO.Path.GetFullPath(System.IO.Path.Combine(appHostDir, "..", "..", "app")),   // typical: brain/Neuro... -> root/app
        System.IO.Path.GetFullPath(System.IO.Path.Combine(appHostDir, "..", "app")),
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "app")),
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "..", "app")),
    };

    foreach (var c in candidates)
    {
        if (System.IO.Directory.Exists(c) && System.IO.File.Exists(System.IO.Path.Combine(c, "pubspec.yaml")))
            return c;
    }

    // 3. Walk up from AppHostDirectory looking for an "app" folder with Flutter marker
    var dir = new System.IO.DirectoryInfo(appHostDir);
    for (int i = 0; i < 6 && dir != null; i++)
    {
        var candidate = System.IO.Path.Combine(dir.FullName, "app");
        if (System.IO.Directory.Exists(candidate) && System.IO.File.Exists(System.IO.Path.Combine(candidate, "pubspec.yaml")))
            return System.IO.Path.GetFullPath(candidate);

        dir = dir.Parent;
    }

    return null;
}

if (ctx.EnableMcp)
{
    // Expose DigitalBrain MCP (stdio tools) as resource so aspire mcp call can discover registered tools: run_closed_loop, ask_ino, publish_to_marketplace, list_marketplace, etc.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp")
        .WithReference(ctx.OrleansClient)
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm);
}

if (IsEnabled("DIGITALBRAIN_ENABLE_TELEGRAM"))
{
    // Optional packed integration. Keep it out of the default product path until a real host replaces the placeholder.
    ctx.AddTelegramBot("telegram-bot");
}

if (IsEnabled("DIGITALBRAIN_ENABLE_DIAGNOSTIC_GATEWAY"))
{
    // Optional legacy diagnostic gateway. The kernel hosts the product gRPC/surface gateway by default.
    builder.AddProject<Projects.DigitalBrain_Gateway>("gateway")
        .WithReference(ctx.OrleansClient)
        .WithReference(ctx.ClusteringTable)
        .WithExternalHttpEndpoints();
}

kernel.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
kernel.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

// Inject Ollama LLM config so AddDigitalBrainChat registers IChatClient in the Aspire-hosted kernel.
// Cloud path: override DigitalBrain__Llm__Provider=azureopenai via DIGITALBRAIN_ENV or appsettings.
kernel.WithEnvironment("DigitalBrain__Llm__Provider", "ollama");
kernel.WithEnvironment("DigitalBrain__Llm__Model", ctx.LlmModel);
kernel.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
    ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));
if (ctx.EnableOrleansDashboard)
{
    kernel.WithEnvironment("ORLEANS_DASHBOARD_PORT", (ctx.OrleansDashboardPort ?? 8080).ToString());
}

builder.Build().Run();

static bool IsEnabled(string name) =>
    string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.OrdinalIgnoreCase);
