namespace DigitalBrain.Kernel.Foundry;

public sealed class AzureResourceController : IResourceController
{
    private readonly ILogger<AzureResourceController> _logger;
    private readonly bool _dryRun;

    public string? LastReason { get; private set; }

    public AzureResourceController(ILogger<AzureResourceController> logger, bool dryRun = false)
    {
        _logger = logger;
        _dryRun = dryRun;
    }

    public Task RestartSiloAsync(string reason)
    {
        LastReason = reason;
        _logger.LogWarning("Cloud self-update: requesting ACA revision restart ({Reason}).", reason);
        if (_dryRun)
            return Task.CompletedTask;

        // TODO Task 10: call Azure management API / `az containerapp revision restart`
        // via managed identity to trigger a real ACA revision restart.
        // Example: await _azureClient.ContainerApps.RestartRevisionAsync(resourceGroup, appName, revisionName);
        return Task.CompletedTask;
    }
}
