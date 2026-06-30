using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;

namespace Aspire.Hosting.DigitalBrain;

public sealed class DigitalBrainContext
{
    public required IResourceBuilder<DigitalBrainResource> Resource { get; init; }
    public required OrleansService Orleans { get; init; }
    public required object Llm { get; init; }
    public required OrleansServiceClient OrleansClient { get; init; }
    public required int KernelReplicas { get; init; }
    public required bool UseLocalMarketplace { get; init; }

    // The resolved LLM model name (e.g. "qwen2.5-coder:1.5b") for env injection
    public required string LlmModel { get; init; }

    // Ollama container http endpoint for DigitalBrain__Llm__OllamaEndpoint injection
    public required EndpointReference OllamaEndpoint { get; init; }

    // Storage resources exposed so AppHost can wire WithReference on silo
    public required IResourceBuilder<AzureBlobStorageResource> GrainBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> JournalBlobs { get; init; }
    public required IResourceBuilder<AzureTableStorageResource> ClusteringTable { get; init; }

    // For encapsulated dashboard + MCP (WithOrleansDashboard / WithMcp)
    public bool EnableOrleansDashboard { get; set; }
    public int? OrleansDashboardPort { get; set; }
    public bool EnableMcp { get; set; }
}

public static class DigitalBrainBuilderExtensions
{
    public static DigitalBrainContext AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "digitalbrain",
        Action<DigitalBrainOptions>? configure = null)
    {
        var options = new DigitalBrainOptions();
        configure?.Invoke(options);
        if (int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_KERNEL_REPLICAS"), out var replicaOverride) && replicaOverride > 0)
        {
            options.KernelReplicas = replicaOverride;
        }

        var resource = new DigitalBrainResource(name);
        var db = builder.AddResource(resource);

        var llmModel = options.LlmModel ?? "qwen2.5-coder:1.5b";

        var storage = builder.AddAzureStorage("storage").RunAsEmulator();
        var clusteringTable = storage.AddTables("clustering");
        var grainBlobs = storage.AddBlobs("grainstate");
        var journalBlobs = storage.AddBlobs("journal");

        var orleans = builder.AddOrleans("kernel")
            .WithClustering(clusteringTable)
            .WithGrainStorage("Default", grainBlobs);

        var ollama = builder.AddOllama("ollama")
            .WithGPUSupport()
            .WithDataVolume();
        var qwen = ollama.AddModel("qwen", llmModel);

        return new DigitalBrainContext
        {
            Resource = db,
            Orleans = orleans,
            Llm = qwen,
            OrleansClient = orleans.AsClient(),
            KernelReplicas = options.KernelReplicas,
            UseLocalMarketplace = options.UseLocalMarketplace,
            LlmModel = llmModel,
            OllamaEndpoint = ollama.GetEndpoint("http"),
            EnableOrleansDashboard = options.EnableOrleansDashboard,
            OrleansDashboardPort = options.OrleansDashboardPort,
            EnableMcp = options.EnableMcp,
            GrainBlobs = grainBlobs,
            JournalBlobs = journalBlobs,
            ClusteringTable = clusteringTable
        };
    }

    public static DigitalBrainContext WithOrleansDashboard(this DigitalBrainContext ctx, int? port = null)
    {
        ctx.EnableOrleansDashboard = true;
        if (port.HasValue) ctx.OrleansDashboardPort = port;
        return ctx;
    }

    public static DigitalBrainContext WithMcp(this DigitalBrainContext ctx, int? port = null)
    {
        ctx.EnableMcp = true;
        return ctx;
    }

    /// <summary>
    /// Wires a kernel project with the core kernel features out of the box:
    /// marketplace, dynamic UI surfaces, journals, clustering, LLM, and replica count for HA.
    /// This makes the kernel (company brain) provide built-in capabilities (embodiment, status, tasks, etc.)
    /// immediately when the silo starts.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WireKernelSilo(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        kernel = kernel
            .WithReference(ctx.Orleans)
            .WithReference(ctx.ClusteringTable)
            .WithReference(ctx.GrainBlobs)
            .WithReference(ctx.JournalBlobs)
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
            .WithEndpoint(name: "grpc", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
            .WithEndpoint(name: "web", scheme: "http", env: "DIGITALBRAIN_WEB_PORT", isProxied: true)
            .WithExternalHttpEndpoints()
            .WithReplicas(ctx.KernelReplicas);

        kernel.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
        kernel.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

        // LLM for kernel built-ins (INO, status diagnosis, code gen, tasks)
        kernel.WithEnvironment("DigitalBrain__Llm__Provider", "ollama");
        kernel.WithEnvironment("DigitalBrain__Llm__Model", ctx.LlmModel);
        kernel.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
            ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));

        if (ctx.EnableOrleansDashboard && ctx.OrleansDashboardPort.HasValue)
        {
            kernel.WithEnvironment("ORLEANS_DASHBOARD_PORT", ctx.OrleansDashboardPort.Value.ToString());
        }

        return kernel;
    }

    /// <summary>
    /// Flutter as marketplace pack + Aspire integration. Call from AppHost or brain.cs-driven launcher when the Flutter pack (DigitalBrain.UI.AspireFlutter) is installed.
    /// Starts Flutter (windows or web-server) wired to brain for live surfaces/RfwCards. Enables full packing/distribution/reuse of the UI client as a NeuroPack.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddFlutterClient(
        this DigitalBrainContext ctx,
        string name,
        string flutterAppPath,
        string target = "windows")
    {
        var cmd = ctx.Resource.ApplicationBuilder.Configuration["DigitalBrain:FlutterCommand"]
            ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
            ?? "flutter";

        return ctx.Resource.ApplicationBuilder.AddExecutable(
                name,
                cmd,
                flutterAppPath,
                "run",
                "-d",
                target)
            .WithReference(ctx.OrleansClient)
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
            .WithEnvironment("DIGITALBRAIN_UI_PACK", "DigitalBrain.UI.AspireFlutter")
            .WithEnvironment("DIGITALBRAIN_UI_TIER1_RESTART_REQUIRED", "true");
    }

    /// <summary>
    /// Wires the Telegram transport (<c>DigitalBrain.Telegram.Transport</c>) as an Aspire resource that bridges
    /// Telegram updates to the kernel gateway over gRPC. The transport boots no-op without a bot token, so the
    /// resource can be present from startup and configured later (token supplied at launch or via the in-app flow)
    /// with no AppHost restart.
    /// </summary>
    /// <param name="transport">The transport project, created in the AppHost via <c>AddProject&lt;Projects.DigitalBrain_Telegram_Transport&gt;(name)</c> so the generated <c>Projects.*</c> metadata type resolves.</param>
    /// <param name="kernel">The kernel/gateway resource whose gRPC endpoint the transport calls. Its grpc endpoint is injected as the gateway address.</param>
    /// <param name="botToken">Optional secret parameter carrying the Telegram bot token. When omitted (no token configured), the transport runs idle.</param>
    /// <param name="internalServiceKey">Shared service-to-service secret (same value injected into the kernel) that the transport presents on the secrets-returning <c>GetPackConfig</c> RPC. Server/transport-only — never exposed to the Flutter client config.</param>
    public static IResourceBuilder<ProjectResource> WireTelegramTransport(
        this DigitalBrainContext ctx,
        IResourceBuilder<ProjectResource> transport,
        IResourceBuilder<ProjectResource> kernel,
        IResourceBuilder<ParameterResource>? botToken = null,
        IResourceBuilder<ParameterResource>? internalServiceKey = null)
    {
        var kernelGrpc = kernel.GetEndpoint("grpc");

        transport = transport
            .WithReference(ctx.OrleansClient)
            .WithReference(kernel)
            .WaitFor(kernel)
            .WithEnvironment("DigitalBrain__GatewayAddress",
                ReferenceExpression.Create($"http://{kernelGrpc.Property(EndpointProperty.Host)}:{kernelGrpc.Property(EndpointProperty.Port)}"));

        if (botToken is not null)
        {
            transport = transport.WithEnvironment("Telegram__BotToken", botToken);
        }

        if (internalServiceKey is not null)
        {
            transport = transport.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);
        }

        // Tell the transport which marketplace pack's stored config carries its bot token.
        // Matches the pack name in MarketplaceSeeds and the ConfigPack constant inside the pack code.
        transport = transport
            .WithEnvironment("Telegram__PackName", "DigitalBrain.Telegram.Responder")
            .WithEnvironment("Telegram__ConfigScope", "default");

        return transport;
    }
}

public sealed class DigitalBrainOptions
{
    public string? LlmModel { get; set; }
    public int KernelReplicas { get; set; } = 3;
    public bool UseLocalMarketplace { get; set; } = true;

    public bool EnableOrleansDashboard { get; set; } = true;
    public int? OrleansDashboardPort { get; set; } = 8080;
    public bool EnableMcp { get; set; } = true;
}
