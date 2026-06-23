using DeploymentKit.Exceptions;
using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using DeploymentKit.Utilities;
using Pulumi.AzureNative.Resources;
using System.Diagnostics;

namespace DeploymentKit.Infrastructure;

/// <summary>
/// Orchestrates the creation of all infrastructure resources in the correct order
/// </summary>
public class InfrastructureOrchestrator(ILogger<InfrastructureOrchestrator> logger, ICorrelationIdService correlationIdService, IPreDeploymentValidator preDeploymentValidator, INetworkService networkService, IContainerRegistryService containerRegistryService, IDatabaseService databaseService, ICacheService cacheService, IMonitoringService monitoringService, IStorageService storageService, IContainerAppsService containerAppsService, IKeyVaultService keyVaultService, IApplicationGatewayService applicationGatewayService, IDomainOptimizationService domainOptimizationService, IEventHubsService eventHubsService, IFrontDoorDeployer frontDoorDeployer, IOpenAiService openAiService, ICertificateManagementService? certificateManagementService = null, IDatabaseMigrationService? databaseMigrationService = null)
{
    private readonly ILogger<InfrastructureOrchestrator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ICorrelationIdService _correlationIdService = correlationIdService ?? throw new ArgumentNullException(nameof(correlationIdService));
    private readonly IPreDeploymentValidator _preDeploymentValidator = preDeploymentValidator ?? throw new ArgumentNullException(nameof(preDeploymentValidator));
    private readonly INetworkService _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
    private readonly IContainerRegistryService _containerRegistryService = containerRegistryService ?? throw new ArgumentNullException(nameof(containerRegistryService));
    private readonly IDatabaseService _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    private readonly ICacheService _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    private readonly IMonitoringService _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
    private readonly IStorageService _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    private readonly IContainerAppsService _containerAppsService = containerAppsService ?? throw new ArgumentNullException(nameof(containerAppsService));
    private readonly IKeyVaultService _keyVaultService = keyVaultService ?? throw new ArgumentNullException(nameof(keyVaultService));
    private readonly IApplicationGatewayService _applicationGatewayService = applicationGatewayService ?? throw new ArgumentNullException(nameof(applicationGatewayService));
    private readonly IDomainOptimizationService _domainOptimizationService = domainOptimizationService ?? throw new ArgumentNullException(nameof(domainOptimizationService));
    private readonly IEventHubsService _eventHubsService = eventHubsService ?? throw new ArgumentNullException(nameof(eventHubsService));
    private readonly IFrontDoorDeployer _frontDoorDeployer = frontDoorDeployer ?? throw new ArgumentNullException(nameof(frontDoorDeployer));
    private readonly IOpenAiService _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));

    /// <summary>
    /// Deploys the complete 
    /// </summary>
    /// <param name="settings">Infrastructure configuration settings</param>
    /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
    /// <returns>Complete infrastructure outputs</returns>
    public async Task<InfrastructureDeploymentOutputs> DeployAsync(InfrastructureSettings settings, CancellationToken cancellationToken = default)
    {
        // Generate and set correlation ID for the entire deployment
        var correlationId = _correlationIdService.GenerateCorrelationId();
        _correlationIdService.SetCorrelationId(correlationId);

        var stopwatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["Environment"] = settings.Environment,
            ["Location"] = settings.Location,
            ["Operation"] = "InfrastructureDeployment"
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Starting DeploymentKit infrastructure deployment with CorrelationId: {CorrelationId} for environment: {Environment} in location: {Location}",
                correlationId, settings.Environment, settings.Location);

            ValidateSettings(settings);
            _logger.LogDebug("Infrastructure settings validation completed successfully for CorrelationId: {CorrelationId}", correlationId);

            _logger.LogInformation("Running comprehensive pre-deployment validation (settings + resource naming)...");
            var preDeploymentValidation = await _preDeploymentValidator.ValidateAllAsync(settings);
            preDeploymentValidation.PrintSummary(_logger);

            if (!preDeploymentValidation.IsValid)
                throw new ConfigurationValidationException($"Pre-deployment validation failed with {preDeploymentValidation.Errors.Count} error(ContainerAppIngressExtensions). Please fix the configuration issues before deployment.", "PreDeploymentValidation", "ConfigurationValidation");

            var resourceGroup = await CreateResourceGroupAsync(settings, correlationId, cancellationToken);
            _logger.LogInformation("Resource group creation completed in {ElapsedMs}ms for CorrelationId: {CorrelationId}",
                stopwatch.ElapsedMilliseconds, correlationId);

            cancellationToken.ThrowIfCancellationRequested();

            var independentServicesStopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Creating services in prioritized order for CorrelationId: {CorrelationId}...", correlationId);

            // Network must be created first to support VNet integration for Database and Cache
            var networkTask = CreateServiceWithLogging("Network", () => _networkService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);

            // Other independent services can be created in parallel with Network
            var monitoringTask = CreateServiceWithLogging("Monitoring", () => _monitoringService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);
            var containerRegistryTask = CreateServiceWithLogging("ContainerRegistry", () => _containerRegistryService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);
            var storageTask = CreateServiceWithLogging("Storage", () => _storageService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);
            var keyVaultTask = CreateServiceWithLogging("KeyVault", () => _keyVaultService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);
            var eventHubsTask = CreateServiceWithLogging("EventHubs", () => _eventHubsService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);
            var cacheTask = CreateServiceWithLogging("Cache", () => _cacheService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);
            var openAiTask = CreateServiceWithLogging("OpenAI", () => _openAiService.CreateAsync(settings, resourceGroup.Name, cancellationToken), correlationId);

            // Wait for Network to be available for VNet integrated services
            var network = await networkTask;

            // Database service relies on Network for VNet integration
            var databaseTask = CreateServiceWithLogging("Database", () => _databaseService.CreateAsync(settings, resourceGroup.Name, network, cancellationToken), correlationId);

            await Task.WhenAll(monitoringTask, containerRegistryTask, storageTask, keyVaultTask, eventHubsTask, databaseTask, cacheTask, openAiTask);

            var monitoring = await monitoringTask;
            var containerRegistry = await containerRegistryTask;
            var storage = await storageTask;
            var keyVault = await keyVaultTask;
            var eventHubs = await eventHubsTask;
            var database = await databaseTask;
            var cache = await cacheTask;
            var openAi = await openAiTask;

            _logger.LogInformation("All independent services created in {ElapsedMs}ms for CorrelationId: {CorrelationId}", independentServicesStopwatch.ElapsedMilliseconds, correlationId);

            cancellationToken.ThrowIfCancellationRequested();

            switch (settings.Migration)
            {
                case { Enabled: true, AutoRunOnDeployment: true } when databaseMigrationService != null:
                {
                    var migrationStopwatch = Stopwatch.StartNew();
                    _logger.LogInformation("Running database migrations for CorrelationId: {CorrelationId}...", correlationId);

                    try
                    {
                        var migrationOutputs = await databaseMigrationService.RunMigrationsAsync(settings, database, resourceGroup.Name, cancellationToken);
                        _logger.LogInformation("Database migrations completed in {ElapsedMs}ms for CorrelationId: {CorrelationId}, Success: {Success}, MigrationsApplied: {MigrationsApplied}",
                            migrationStopwatch.ElapsedMilliseconds, correlationId, migrationOutputs.Success, migrationOutputs.MigrationsApplied);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Database migration failed for CorrelationId: {CorrelationId}", correlationId);

                        if (settings.Migration.FailOnError)
                        {
                            throw;
                        }

                        _logger.LogWarning("Continuing deployment despite migration failure (FailOnError=false) for CorrelationId: {CorrelationId}", correlationId);
                    }

                    break;
                }
                case { Enabled: true, AutoRunOnDeployment: false }:
                    _logger.LogInformation("Database migrations are configured but AutoRunOnDeployment is false. Migrations must be run manually. CorrelationId: {CorrelationId}", correlationId);
                    break;
                default:
                    _logger.LogInformation("Database migrations are not configured, skipping migration execution for CorrelationId: {CorrelationId}", correlationId);
                    break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            FrontDoorOutputs? frontDoorFoundation = null;
            if (settings.FrontDoor?.Enabled == true)
            {
                frontDoorFoundation = await CreateServiceWithLogging(
                    "FrontDoorFoundation",
                    () => _frontDoorDeployer.CreateFoundationAsync(settings, resourceGroup.Name, cancellationToken),
                    correlationId);
            }

            // Provision SSL certificates if custom domain is enabled
            // Start this in parallel with Container Apps as it only depends on KeyVault (already created)
            Task<CertificateOutputs?> certificateTask = Task.FromResult<CertificateOutputs?>(null);
            if (settings.CustomDomain?.Enabled == true && certificateManagementService != null)
            {
                certificateTask = ProvisionCertificateAsync(settings, resourceGroup.Name, keyVault.ResourceId, correlationId, cancellationToken);
            }
            else if (settings.CustomDomain?.Enabled == true)
            {
                _logger.LogWarning("Certificate management service not available. Custom domain configured but certificates will not be provisioned.");
            }

            var containerAppsStopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Creating Container Apps for CorrelationId: {CorrelationId}...", correlationId);
            Input<string>? azureFrontDoorId = null;
            if (frontDoorFoundation?.FrontDoorId != null)
            {
                azureFrontDoorId = frontDoorFoundation.FrontDoorId;
            }
            var containerApps = await _containerAppsService.CreateAsync(
                settings,
                resourceGroup.Name,
                monitoring,
                containerRegistry,
                database,
                cache,
                network,
                eventHubs,
                storage: storage,
                openAi: openAi,
                keyVault: keyVault,
                cancellationToken: cancellationToken,
                azureFrontDoorId: azureFrontDoorId);
            _logger.LogInformation("Container Apps created in {ElapsedMs}ms for CorrelationId: {CorrelationId}", containerAppsStopwatch.ElapsedMilliseconds, correlationId);

            cancellationToken.ThrowIfCancellationRequested();

            FrontDoorOutputs? frontDoor = null;
            if (frontDoorFoundation != null)
            {
                frontDoor = await CreateServiceWithLogging(
                    "FrontDoorRouting",
                    () => _frontDoorDeployer.ConfigureRoutingAsync(settings, resourceGroup.Name, frontDoorFoundation, storage, containerApps, cancellationToken),
                    correlationId);
            }

            // Wait for certificate task before creating App Gateway as it depends on it
            var certificate = await certificateTask;

            cancellationToken.ThrowIfCancellationRequested();

            // Conditionally create Application Gateway if enabled
            ApplicationGatewayOutputs? applicationGateway = null;
            if (settings.ApplicationGateway?.Enabled ?? false)
            {
                var appGatewayStopwatch = Stopwatch.StartNew();
                _logger.LogInformation("Creating Application Gateway for CorrelationId: {CorrelationId}...", correlationId);
                applicationGateway = await _applicationGatewayService.CreateAsync(settings, resourceGroup.Name, network, containerApps, certificate, cancellationToken);
                _logger.LogInformation("Application Gateway created in {ElapsedMs}ms for CorrelationId: {CorrelationId}", appGatewayStopwatch.ElapsedMilliseconds, correlationId);
            }
            else
            {
                _logger.LogInformation("Application Gateway creation skipped (not enabled) for CorrelationId: {CorrelationId}", correlationId);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Conditionally create Domain Optimization if Application Gateway exists
            DomainOptimizationOutputs? domainOptimization = null;
            if (applicationGateway != null)
            {
                var domainOptStopwatch = Stopwatch.StartNew();
                _logger.LogInformation("Optimizing domain configuration for CorrelationId: {CorrelationId}...", correlationId);
                domainOptimization = await _domainOptimizationService.CreateAsync(settings, resourceGroup.Name, applicationGateway, cancellationToken);
                _logger.LogInformation("Domain optimization completed in {ElapsedMs}ms for CorrelationId: {CorrelationId}", domainOptStopwatch.ElapsedMilliseconds, correlationId);
            }
            else
            {
                _logger.LogInformation("Domain optimization skipped (Application Gateway not enabled) for CorrelationId: {CorrelationId}", correlationId);
            }

            var outputs = new InfrastructureDeploymentOutputs
            {
                ResourceGroupName = resourceGroup.Name,
                Network = network,
                ContainerRegistry = containerRegistry,
                Database = database,
                Cache = cache,
                Monitoring = monitoring,
                Storage = storage,
                ContainerApps = containerApps,
                KeyVault = keyVault,
                Certificate = certificate,
                ApplicationGateway = applicationGateway,
                DomainOptimization = domainOptimization,
                EventHubs = eventHubs,
                OpenAi = openAi,
                FrontDoor = frontDoor,
                ApiUrl = domainOptimization?.OptimizedDomainUrl ?? containerApps.ApiAppUrl,
                WebsiteUrl = frontDoor?.WebsiteCustomDomainHostName != null
                    ? frontDoor.WebsiteCustomDomainHostName.Apply(host => string.IsNullOrWhiteSpace(host) ? string.Empty : $"https://{host}")
                    : storage.WebsitePrimaryEndpoint,
                MiniAppUrl = frontDoor?.MiniAppCustomDomainHostName != null
                    ? frontDoor.MiniAppCustomDomainHostName.Apply(host => string.IsNullOrWhiteSpace(host) ? string.Empty : $"https://{host}")
                    : storage.MiniAppPrimaryEndpoint,
                JobsInternalFqdn = containerApps.JobsInternalFqdn,
                AcrLoginServer = containerRegistry.LoginServer,
                PostgresHost = database.FullyQualifiedDomainName,
                RedisHost = cache.HostName
            };

            stopwatch.Stop();

            _logger.LogInformation(
                "Successfully completed DeploymentKit infrastructure deployment for environment: {Environment} in {TotalElapsedMs}ms with CorrelationId: {CorrelationId}. Resources created: ResourceGroup={ResourceGroupName}, Database={DatabaseName}, Cache={CacheName}",
                settings.Environment,
                stopwatch.ElapsedMilliseconds,
                correlationId,
                settings.ResourceGroupName,
                database.ServerName,
                cache.Name);

            return outputs;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Infrastructure deployment was cancelled for environment: {Environment} after {ElapsedMs}ms with CorrelationId: {CorrelationId}", settings.Environment, stopwatch.ElapsedMilliseconds, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to deploy DeploymentKit infrastructure for environment: {Environment} after {ElapsedMs}ms with CorrelationId: {CorrelationId}. Error: {ErrorMessage}", settings.Environment, stopwatch.ElapsedMilliseconds, correlationId, ex.Message);
            throw new InfrastructureException($"Infrastructure deployment failed for environment '{settings.Environment}' (CorrelationId: {correlationId})", ex, "DeploymentKitDeployment", "InfrastructureStack", settings.Environment);
        }
    }

    /// <summary>
    /// Creates a service with comprehensive logging and timing
    /// </summary>
    private async Task<T> CreateServiceWithLogging<T>(string serviceName, Func<Task<T>> serviceCreation, string correlationId)
    {
        var serviceStopwatch = Stopwatch.StartNew();

        using var serviceScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = serviceName,
            ["CorrelationId"] = correlationId
        });

        try
        {
            _logger.LogDebug("Starting {ServiceName} creation for CorrelationId: {CorrelationId}", serviceName, correlationId);
            var result = await serviceCreation();
            serviceStopwatch.Stop();

            _logger.LogInformation("{ServiceName} created successfully in {ElapsedMs}ms for CorrelationId: {CorrelationId}",
                serviceName, serviceStopwatch.ElapsedMilliseconds, correlationId);

            return result;
        }
        catch (Exception ex)
        {
            serviceStopwatch.Stop();
            _logger.LogError(ex, "Failed to create {ServiceName} after {ElapsedMs}ms for CorrelationId: {CorrelationId}. Error: {ErrorMessage}",
                serviceName, serviceStopwatch.ElapsedMilliseconds, correlationId, ex.Message);
            throw;
        }
    }

    private async Task<CertificateOutputs?> ProvisionCertificateAsync(
        InfrastructureSettings settings,
        Input<string> resourceGroupName,
        Input<string> keyVaultResourceId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var certStopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Provisioning SSL certificate for domain: {Domain}, CorrelationId: {CorrelationId}...",
            settings.CustomDomain?.Name, correlationId);

        var cert = await certificateManagementService!.ProvisionCertificateAsync(
            settings,
            resourceGroupName,
            keyVaultResourceId,
            cancellationToken);

        _logger.LogInformation("SSL certificate provisioned in {ElapsedMs}ms for CorrelationId: {CorrelationId}",
            certStopwatch.ElapsedMilliseconds, correlationId);

        return cert;
    }

    /// <summary>
    /// Creates the resource group for the infrastructure
    /// </summary>
    private Task<ResourceGroup> CreateResourceGroupAsync(InfrastructureSettings settings, string correlationId, CancellationToken cancellationToken = default)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "ResourceGroupCreation",
            ["CorrelationId"] = correlationId
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Use the resource group name from settings (already set by InfrastructureBuilder)
            // instead of regenerating it to avoid naming mismatches
            var resourceGroupName = settings.ResourceGroupName;

            _logger.LogInformation("Creating resource group: {ResourceGroupName} in location: {Location} for CorrelationId: {CorrelationId}",
                resourceGroupName, settings.Location, correlationId);

            var resourceGroup = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
            {
                ResourceGroupName = resourceGroupName,
                Location = settings.Location,
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, "resource-group")
            });

            _logger.LogInformation("Successfully created resource group: {ResourceGroupName} for CorrelationId: {CorrelationId}",
                resourceGroupName, correlationId);

            return Task.FromResult(resourceGroup);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create resource group for environment: {Environment} with CorrelationId: {CorrelationId}. Error: {ErrorMessage}",
                settings.Environment, correlationId, ex.Message);

            throw new ResourceCreationException(
                $"Failed to create resource group for environment '{settings.Environment}' (CorrelationId: {correlationId})",
                ex,
                "ResourceGroup",
                "ResourceGroup",
                settings.Environment,
                correlationId,
                "RG_CREATION_FAILED");
        }
    }

    /// <summary>
    /// Validates the infrastructure settings
    /// </summary>
    private void ValidateSettings(InfrastructureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _logger.LogDebug("Validating infrastructure settings for environment: {Environment}", settings.Environment);

        if (string.IsNullOrWhiteSpace(settings.Environment))
        {
            _logger.LogError("Environment validation failed: Environment is required");
            throw new ConfigurationValidationException("Environment is required", "Environment", "InfrastructureSettings");
        }

        if (string.IsNullOrWhiteSpace(settings.Location))
        {
            _logger.LogError("Location validation failed: Location is required");
            throw new ConfigurationValidationException("Location is required", "Location", "InfrastructureSettings");
        }

        if (string.IsNullOrWhiteSpace(settings.NamingPrefix))
        {
            _logger.LogError("NamingPrefix validation failed: NamingPrefix is required");
            throw new ConfigurationValidationException("NamingPrefix is required", "NamingPrefix", "InfrastructureSettings");
        }

        if (string.IsNullOrWhiteSpace(settings.ResourceGroupName))
        {
            _logger.LogError("ResourceGroupName validation failed: ResourceGroupName is required");
            throw new ConfigurationValidationException("ResourceGroupName is required", "ResourceGroupName", "InfrastructureSettings");
        }

        if (string.IsNullOrWhiteSpace(settings.SubscriptionId))
        {
            _logger.LogError("SubscriptionId validation failed: SubscriptionId is required");
            throw new ConfigurationValidationException("SubscriptionId is required", "SubscriptionId", "InfrastructureSettings");
        }

        _logger.LogDebug("Infrastructure settings validation completed successfully for environment: {Environment}", settings.Environment);
    }
}

