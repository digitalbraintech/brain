using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Orleans;
using CommunityToolkit.Aspire.Hosting.Ollama;

namespace Aspire.Hosting.DigitalBrain;

public sealed class DigitalBrainContext
{
    public required IResourceBuilder<DigitalBrainResource> Resource { get; init; }
    public required OrleansService Orleans { get; init; }
    public required object Llm { get; init; }
    public required OrleansServiceClient OrleansClient { get; init; }
    public required int KernelReplicas { get; init; }
    public required bool UseLocalMarketplace { get; init; }
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
            UseLocalMarketplace = options.UseLocalMarketplace
        };
    }
}

public sealed class DigitalBrainOptions
{
    public string? LlmModel { get; set; }
    public int KernelReplicas { get; set; } = 3;
    public bool UseLocalMarketplace { get; set; } = true;
}