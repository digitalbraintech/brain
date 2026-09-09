using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Scripting.Applications;

public sealed class ShippedApplicationBootstrap(
    ApplicationAuthoringService authoring,
    IGrainFactory grains,
    IConfiguration configuration) : BackgroundService
{
    public const string SourceConfigurationKey = "DigitalBrain:StartupApplication:Source";
    private static readonly PrincipalId OwnerPrincipal = new(
        new Guid("0000dead-0000-0000-0000-000000000001"));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var source = configuration[SourceConfigurationKey]
            ?? Path.Combine(AppContext.BaseDirectory, "scripts", "start.cs");
        var owner = configuration[DigitalBrainNames.Owner] ?? DigitalBrainNames.DefaultOwner;
        var actor = new ActorContext(OwnerPrincipal, "owner");
        await using var brain = DigitalBrainClient.Connect(grains, owner, actor);
        using var actorScope = VerifiedActor.Enter(actor);
        _ = await new ShippedApplicationInstaller(authoring).EnsureActiveAsync(
            brain, "start", source, stoppingToken).ConfigureAwait(false);
    }
}
