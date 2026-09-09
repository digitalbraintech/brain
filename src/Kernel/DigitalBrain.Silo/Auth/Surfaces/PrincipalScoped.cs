using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Kernel;

// Host-side alias for PrincipalPartition — chat/surface map authenticated principals here.
internal static class PrincipalScoped
{
    public static string InstanceName(PrincipalId principal, string localName)
        => PrincipalPartition.InstanceName(principal, localName);
}

