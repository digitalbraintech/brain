using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;

namespace DigitalBrain.Abstractions;

public sealed partial class DigitalBrainClient
{
    public ApplicationDefinition Application(string key) => _transport.Application(key);
}

public static class ApplicationClientExtensions
{
    public static ApplicationDefinition Application(this IDigitalBrain brain, string key)
        => brain is DigitalBrainClient client ? client.Application(key)
            : brain is BorrowedDigitalBrain borrowed ? borrowed.Inner.Application(key)
            : throw new InvalidOperationException("This execution view does not support application authoring.");
}

internal sealed partial class DigitalBrainClientTransport
{
    internal ApplicationDefinition Application(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Contains('/', StringComparison.Ordinal)) { throw new ArgumentException("Application keys cannot contain '/'.", nameof(key)); }
        using var scope = EnterConnectionActor();
        var actor = VerifiedActor.Current ?? throw new InvalidOperationException("Application authoring requires an authenticated principal.");
        return new(_grains.GetGrain<IApplicationKernel>($"{Owner.Value}/{actor.PrincipalId}/{key}"), actor, _grains);
    }
}
