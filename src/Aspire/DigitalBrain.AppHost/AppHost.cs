using Aspire.Hosting;
using DigitalBrain.AI;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain)
    .AddModule<AIModule>();

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
