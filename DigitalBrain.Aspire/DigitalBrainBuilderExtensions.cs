using Aspire.Hosting.ApplicationModel;
using System;

namespace Aspire.Hosting;

/// <summary>
/// Fluent extensions for adding a self-aware DigitalBrain (core kernel + marketplace + LLM + TUI).
/// Follows CommunityToolkit.Aspire.Hosting patterns (minimal, copyable MVP).
/// Heavy wiring stays in consuming AppHost so SDK project remains compile-independent.
/// </summary>
public static class DigitalBrainBuilderExtensions
{
    public static IResourceBuilder<DigitalBrainResource> AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "digitalbrain",
        Action<DigitalBrainOptions>? configure = null)
    {
        var options = new DigitalBrainOptions();
        configure?.Invoke(options);

        var resource = new DigitalBrainResource(name);
        return builder.AddResource(resource)
            // Consumers call the real AddRedis/AddOrleans/AddOllama/AddProject in AppHost using options
            // This resource acts as the anchor for .With* chaining and future custom behavior.
            ;
    }

    public static IResourceBuilder<DigitalBrainResource> WithLLM(
        this IResourceBuilder<DigitalBrainResource> builder,
        string modelTag = "qwen2.5-coder:1.5b")
    {
        // Intent captured via options at Add time for MVP. Real resources can read annotations later.
        return builder;
    }

    public static IResourceBuilder<DigitalBrainResource> WithTUI(
        this IResourceBuilder<DigitalBrainResource> builder,
        bool explicitStart = true)
    {
        return builder;
    }

    public static IResourceBuilder<DigitalBrainResource> WithMarketplace(
        this IResourceBuilder<DigitalBrainResource> builder,
        Action<MarketplaceConfig> configure)
    {
        return builder;
    }

    public static IResourceBuilder<DigitalBrainResource> AddExperience<TExperience>(
        this IResourceBuilder<DigitalBrainResource> builder,
        Action<ExperienceConfig> configure) where TExperience : class
    {
        return builder;
    }

    public static IResourceBuilder<DigitalBrainResource> WithKernelReplicas(
        this IResourceBuilder<DigitalBrainResource> builder,
        int count)
    {
        return builder;
    }
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