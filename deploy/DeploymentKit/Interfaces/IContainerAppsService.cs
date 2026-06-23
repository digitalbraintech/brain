using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;

namespace DeploymentKit.Interfaces;

/// <summary>
/// Service interface for managing Azure Container Apps infrastructure
/// Provides serverless container hosting with automatic scaling and traffic management
/// </summary>
public interface IContainerAppsService : IInfrastructureService
{
    /// <summary>
    /// Creates Container Apps environment and deploys application containers with full infrastructure dependencies
    /// </summary>
    /// <param name="settings">Infrastructure configuration settings including scaling rules, resource limits, and environment variables</param>
    /// <param name="resourceGroup">Azure resource group name where the container apps will be deployed</param>
    /// <param name="monitoring">Monitoring infrastructure outputs for telemetry and logging integration</param>
    /// <param name="containerRegistry">Container registry outputs for image pulling and authentication</param>
    /// <param name="database">Database infrastructure outputs for connection string configuration</param>
    /// <param name="cache">Cache infrastructure outputs for Redis connection configuration</param>
    /// <param name="network">Network infrastructure outputs for VNet integration and private endpoints</param>
    /// <param name="eventHubs">Event Hubs infrastructure outputs for event streaming configuration</param>
    /// <param name="keyVault">Key Vault infrastructure outputs for secrets management and environment variable injection</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
    /// <returns>Container Apps infrastructure outputs containing app URLs, scaling configurations, and deployment details</returns>
    Task<ContainerAppsOutputs> CreateAsync(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        MonitoringOutputs monitoring,
        ContainerRegistryOutputs containerRegistry,
        DatabaseOutputs database,
        CacheOutputs cache,
        NetworkOutputs network,
        EventHubsOutputs eventHubs,
        StorageOutputs? storage = null,
        OpenAiOutputs? openAi = null,
        KeyVaultOutputs? keyVault = null,
        CancellationToken cancellationToken = default,
        Input<string>? azureFrontDoorId = null);

    /// <summary>
    /// Creates Container Apps environment and deploys application containers using tasks for parallel resource provisioning
    /// </summary>
    /// <param name="settings">Infrastructure configuration settings</param>
    /// <param name="resourceGroup">Azure resource group name</param>
    /// <param name="monitoringTask">Task returning monitoring infrastructure outputs</param>
    /// <param name="containerRegistryTask">Task returning container registry outputs</param>
    /// <param name="databaseTask">Task returning database infrastructure outputs</param>
    /// <param name="cacheTask">Task returning cache infrastructure outputs</param>
    /// <param name="networkTask">Task returning network infrastructure outputs</param>
    /// <param name="eventHubsTask">Task returning event hubs infrastructure outputs</param>
    /// <param name="keyVaultTask">Task returning key vault infrastructure outputs (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="azureFrontDoorId">Azure Front Door ID (optional)</param>
    /// <returns>Container Apps infrastructure outputs</returns>
    Task<ContainerAppsOutputs> CreateAsync(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        Task<MonitoringOutputs> monitoringTask,
        Task<ContainerRegistryOutputs> containerRegistryTask,
        Task<DatabaseOutputs> databaseTask,
        Task<CacheOutputs> cacheTask,
        Task<NetworkOutputs> networkTask,
        Task<EventHubsOutputs> eventHubsTask,
        Task<KeyVaultOutputs?> keyVaultTask,
        CancellationToken cancellationToken = default,
        Input<string>? azureFrontDoorId = null);
}

