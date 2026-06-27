using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Marketplace;

/// <summary>
/// Stub implementation of remote marketplace client.
/// In production this becomes an HttpClient/gRPC client to the private marketplace service.
/// Registered only when UseRemote=true.
/// </summary>
public class RemoteMarketplaceClientStub : IRemoteMarketplaceClient
{
    private readonly ILogger<RemoteMarketplaceClientStub> _logger;

    public RemoteMarketplaceClientStub(ILogger<RemoteMarketplaceClientStub> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(PublishToMarketplace cmd)
    {
        _logger.LogInformation("[REMOTE-MKT] Would publish {Pack}@{Ver} to private marketplace (user={Owner})", cmd.PackName, cmd.Version, cmd.OwnerId);
        return Task.CompletedTask;
    }

    public Task InstallAsync(InstallFromMarketplace cmd)
    {
        _logger.LogInformation("[REMOTE-MKT] Would record install {Pack}@{Ver} by {Buyer} via private marketplace", cmd.PackName, cmd.Version, cmd.BuyerId);
        return Task.CompletedTask;
    }

    public Task<PublishedList> ListAsync()
    {
        _logger.LogInformation("[REMOTE-MKT] Would fetch catalog from private marketplace");
        return Task.FromResult(new PublishedList(Array.Empty<NeuroPack>()));
    }
}