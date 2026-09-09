using Microsoft.AspNetCore.Builder;

namespace DigitalBrain.Sdk;

// A module's slice of the kernel's HTTP pipeline (browser callbacks, webhooks). Registered as a
// singleton from IModule.Configure; the kernel maps every surface in one place, before its gates.
public interface IHttpSurface
{
    void Map(IApplicationBuilder app);
}
