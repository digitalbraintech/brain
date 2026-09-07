using Aspire.Hosting;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.UI;
using DigitalBrain.UI.Aspire.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var fakesEnabled = string.Equals(builder.Configuration[DigitalBrainNames.Fakes], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration[DigitalBrainNames.Fakes], "1", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration[DigitalBrainNames.Mode], DigitalBrainNames.TestingMode, StringComparison.Ordinal);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain)
    .AddModule<UIModule>(ui =>
    {
        ui.WithWindowHost();
    });

if (fakesEnabled)
{
    brain.WithDigitalBrainFakes();
}

// Isolated Aspire runs reuse the persistent Azurite volume while assigning new random silo
// ports. A per-run development cluster avoids trying to contact a dead membership row from the
// previous run; the service id remains stable, so grain and reminder state are still preserved.
var developmentClusterId = builder.Environment.IsDevelopment()
    ? $"digitalbrain-{Guid.NewGuid():N}"
    : null;

var kernel = builder.AddProject<Projects.DigitalBrain_Silo>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: ShellHostingExtensions.HttpEndpointName,
        isProxied: false)
    // Without this, "kernel healthy" means only "process launched": Kestrel binds AFTER the
    // Orleans silo and brain activation finish, so waiters would proceed while 5080 still
    // refuses connections (observed on loaded CI runners).
    .WithHttpHealthCheck("/health", endpointName: ShellHostingExtensions.HttpEndpointName)
    .WithUrlForEndpoint(
        ShellHostingExtensions.HttpEndpointName,
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
            // Local reviews read this checkout. Production has no host workspace unless
            // one is explicitly configured for its owner.
            context.EnvironmentVariables["DigitalBrain__Workspace__RepositoryPath"] =
                builder.Configuration["DigitalBrain:Workspace:RepositoryPath"]
                    ?? Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../.."));
            context.EnvironmentVariables["DigitalBrain__Workspace__Owner"] = ShellHostingExtensions.DefaultOwner;
            context.EnvironmentVariables["DigitalBrain__StartupApplication__Source"] =
                builder.Configuration["DigitalBrain:StartupApplication:Source"]
                    ?? Path.GetFullPath(Path.Combine(
                        builder.AppHostDirectory,
                        "../../Kernel/DigitalBrain.Scripting/scripts/start.cs"));
        }
    });

var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>(ProductSurfaceResources.Mcp)
    .WithMcpServer(ProductSurfaceResources.McpPath, ProductSurfaceResources.McpHttpEndpointName)
    .WithReference(brain.AsClient())
    .WithReference(kernel)
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment(context =>
    {
        if (developmentClusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = developmentClusterId;
        }
    })
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName)
    .WithHttpHealthCheck("/health", endpointName: ProductSurfaceResources.McpHttpEndpointName)
    .WaitFor(kernel);

// Endpoint injection adds no WaitFor(mcp): the kernel must start before the MCP client
// can connect back. Assistant tool discovery opens the connection lazily per session.
kernel.WithEnvironment("DigitalBrain__Mcp__Endpoint", mcp.GetEndpoint(ProductSurfaceResources.McpHttpEndpointName));

builder.Build().Run();
