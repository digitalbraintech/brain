using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Kernel;

internal static class PrincipalSurface
{
    public static string InstanceName(PrincipalId principal, string surfaceName)
        => PrincipalScoped.InstanceName(principal, surfaceName);
}

