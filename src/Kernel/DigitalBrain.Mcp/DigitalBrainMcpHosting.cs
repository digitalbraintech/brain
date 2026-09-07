using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Mcp;

public static class DigitalBrainMcpHosting
{
    // Registers the operations, tools and MCP server. The caller adds a transport:
    // WithHttpTransport for the Silo, WithStreamServerTransport for tests.
    public static IMcpServerBuilder AddDigitalBrainMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<BrainOperations>();
        services.AddHttpContextAccessor();
        services.TryAddScoped(sp => SessionPrincipal.FromHttp(sp.GetRequiredService<IHttpContextAccessor>().HttpContext));
        return services.AddMcpServer(options => options.ServerInfo = new() { Name = "digitalbrain", Version = "0.1" })
            .WithTools<BrainTools>();
    }

    public static IEndpointRouteBuilder MapDigitalBrainMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapMcp(pattern);
        return endpoints;
    }
}
