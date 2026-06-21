using Aspire.Hosting.ApplicationModel;
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

        var redis = builder.AddRedis("redis");
        var orleans = builder.AddOrleans("kernel")
            .WithClustering(redis)
            .WithGrainStorage("Default", redis);
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
            EnableOrleansDashboard = options.EnableOrleansDashboard,
            OrleansDashboardPort = options.OrleansDashboardPort,
            EnableMcp = options.EnableMcp
        };
    }

    // Fluent encapsulation for fast testing + observability (Elon: delete duplicate setup, accelerate live debug)
    // Usage: var ctx = builder.AddDigitalBrain("db").WithOrleansDashboard(8080).WithMcp();
    public static DigitalBrainContext WithOrleansDashboard(this DigitalBrainContext ctx, int? port = null)
    {
        ctx.EnableOrleansDashboard = true;
        if (port.HasValue) ctx.OrleansDashboardPort = port;
        return ctx;
    }

    public static DigitalBrainContext WithMcp(this DigitalBrainContext ctx, int? port = null)
    {
        // Standalone / connectable MCP for runtime inspection + self-improving (SystemStatus + aspire mcp)
        ctx.EnableMcp = true;
        return ctx;
    }
}

public sealed class DigitalBrainOptions
{
    public string? LlmModel { get; set; }
    public int KernelReplicas { get; set; } = 3;
    public bool UseLocalMarketplace { get; set; } = true;

    // Encapsulated observability - add dashboard and MCP hooks
    public bool EnableOrleansDashboard { get; set; } = true;
    public int? OrleansDashboardPort { get; set; } = 8080;
    public bool EnableMcp { get; set; } = true;
}