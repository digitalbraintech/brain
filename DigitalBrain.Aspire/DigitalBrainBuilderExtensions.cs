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
    /// Wires a kernel silo project with the core kernel features out of the box:
    /// marketplace, dynamic UI surfaces, journals, clustering, LLM, and replica count for HA.
    /// This makes the kernel (company brain) provide built-in capabilities (embodiment, status, tasks, etc.)
    /// immediately when the silo starts.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WireKernelSilo(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> silo)
    {
        silo = silo
            .WithReference(ctx.Orleans)
            .WithReference(ctx.ClusteringTable)
            .WithReference(ctx.GrainBlobs)
            .WithReference(ctx.JournalBlobs)
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
            .WithEndpoint(name: "grpc", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
            .WithReplicas(ctx.KernelReplicas);

        silo.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
        silo.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

        // LLM for kernel built-ins (INO, status diagnosis, code gen, tasks)
        silo.WithEnvironment("DigitalBrain__Llm__Provider", "ollama");
        silo.WithEnvironment("DigitalBrain__Llm__Model", ctx.LlmModel);
        silo.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
            ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));

        if (ctx.EnableOrleansDashboard && ctx.OrleansDashboardPort.HasValue)
        {
            silo.WithEnvironment("ORLEANS_DASHBOARD_PORT", ctx.OrleansDashboardPort.Value.ToString());
        }

        return silo;
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
