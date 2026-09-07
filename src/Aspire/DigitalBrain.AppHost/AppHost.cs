using Aspire.Hosting;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var fakesEnabled = string.Equals(builder.Configuration[DigitalBrainNames.Fakes], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration[DigitalBrainNames.Fakes], "1", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration[DigitalBrainNames.Mode], DigitalBrainNames.TestingMode, StringComparison.Ordinal);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain);

if (fakesEnabled)
{
    brain.WithDigitalBrainFakes();
}

var developmentClusterId = builder.Environment.IsDevelopment()
    ? $"digitalbrain-{Guid.NewGuid():N}"
    : null;

builder.AddProject<Projects.DigitalBrain_Silo>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: "http",
        isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithUrlForEndpoint(
        "http",
        endpoint => new ResourceUrlAnnotation
        {
            Url = "/orleans",
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        })
    .WithEnvironment(context =>
    {
        if (developmentClusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = developmentClusterId;
        }
    });

builder.Build().Run();
