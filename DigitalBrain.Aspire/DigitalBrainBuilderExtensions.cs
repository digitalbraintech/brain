using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Orleans;
using System;

namespace Aspire.Hosting;

public sealed class DigitalBrainContext
{
    public required IResourceBuilder<DigitalBrainResource> Resource { get; init; }
    public required IResourceBuilder<IResource> Orleans { get; init; }
    public required IResourceBuilder<IResource> Llm { get; init; }
}

// Fluent extensions for adding a self-aware DigitalBrain (core kernel + marketplace + LLM + TUI).
// Follows CommunityToolkit.Aspire.Hosting patterns (minimal, copyable MVP).
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

        // Core infra encapsulated here based on options
        var redis = builder.AddRedis($"{name}-redis");

        var orleansBuilder = builder.AddOrleans($"{name}-orleans")
            .WithClustering(redis)
            .WithGrainStorage("Default", redis);

        var ollama = builder.AddOllama($"{name}-ollama")
            .WithGPUSupport()
            .WithDataVolume();

        var llmModel = options.LlmModel ?? "qwen2.5-coder:1.5b";
        var llmBuilder = ollama.AddModel($"{name}-llm", llmModel);

        // Marketplace config can be applied by caller on the kernel project via env

        return new DigitalBrainContext
        {
            Resource = db,
            Orleans = (IResourceBuilder<IResource>)(object)orleansBuilder,
            Llm = (IResourceBuilder<IResource>)(object)llmBuilder
        };
    }

    // With* fluent methods can be expanded later for annotations or further config.
    // Core setup is done in AddDigitalBrain based on options.
}

public sealed class DigitalBrainOptions
{
    public string? LlmModel { get; set; }
    public int KernelReplicas { get; set; } = 1;
    public bool ExplicitTuiStart { get; set; } = true;
    public string? GlobalMarketplaceUrl { get; set; }
    public bool UseLocalMarketplace { get; set; } = true;
}

public sealed class MarketplaceConfig
{
    public string? Url { get; set; }
    public bool UseLocal { get; set; } = true;
}

public sealed class ExperienceConfig
{
    public string? PackName { get; set; }
    public string? Version { get; set; }
}