using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

/// The product is single-owner until an actual multi-user boundary is introduced.
/// Keep the stable partition identity without inventing a local credential system.
internal static class HttpActor
{
    private static readonly ActorContext Owner = new(
        new PrincipalId(new Guid("0000dead-0000-0000-0000-000000000001")),
        "owner");

    public static ActorContext Current => VerifiedActor.Current ?? Owner;
}
