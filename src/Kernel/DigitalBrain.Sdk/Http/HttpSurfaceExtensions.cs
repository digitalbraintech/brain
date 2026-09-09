using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Sdk;

public static class HttpSurfaceExtensions
{
    public static IApplicationBuilder UseModuleHttpSurfaces(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        foreach (var surface in app.ApplicationServices.GetServices<IHttpSurface>())
        {
            surface.Map(app);
        }

        return app;
    }
}
