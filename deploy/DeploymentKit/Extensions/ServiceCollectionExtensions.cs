using DeploymentKit.HealthChecks;
using DeploymentKit.Infrastructure;
using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Services;
using DeploymentKit.Settings;
using DeploymentKit.Validators;

namespace DeploymentKit.Extensions;

/// <summary>
/// Extension methods for configuring DeploymentKit deployment services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the core Pulumi deployment services used by DeploymentKit deployment flows.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The configured service collection for method chaining</returns>
    public static IServiceCollection AddPulumiDeploymentCore(this IServiceCollection services)
    {
        // Add utility/core services
        services.AddSingleton<IResourceNamingService, ResourceNamingService>();
        services.AddSingleton<ICorrelationIdService, CorrelationIdService>();
        services.AddSingleton<IResourceNameRegistry, ResourceNameRegistry>();
        services.AddScoped<IResourceNameValidator, ResourceNameValidationService>();
        services.AddScoped<IEnvFileParser, EnvFileParser>();
        services.AddScoped<IAzureAuthenticationService, AzureAuthenticationService>();
        services.AddSingleton<ILegacyEnvironmentBridge, LegacyEnvironmentBridge>();
        services.AddScoped<IInfrastructureConfigurationSource, EscConfigurationSource>();
        services.AddScoped<IInfrastructureConfigurationSource, EnvironmentFallbackConfigurationSource>();
        services.AddScoped<IPulumiConfigLoader, PulumiConfigLoader>();
        services.AddScoped<IPulumiAutomationService, PulumiAutomationService>();
        services.AddScoped<IInfrastructureDeploymentOrchestrator, InfrastructureDeploymentOrchestrator>();
        services.AddScoped<IDeploymentOrchestrator, DeploymentOrchestrator>();
        services.AddScoped<IArmClientProvider, ArmClientProvider>();

        return services;
    }

    /// <summary>
    /// Adds the core Pulumi deployment services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <param name="configureOptions">Configuration action for core SDK options</param>
    /// <returns>The configured service collection for method chaining</returns>
    public static IServiceCollection AddPulumiDeploymentCore(
        this IServiceCollection services,
        Action<InfrastructureSdkOptions> configureOptions)
    {
        var options = new InfrastructureSdkOptions();
        configureOptions(options);
        ValidateSdkOptions(options);

        if (options.EnableStructuredLogging)
        {
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });
        }

        services.AddHttpClient(options.HttpClientName ?? "PulumiDeployment", client =>
        {
            client.Timeout = options.HttpTimeout;
            if (!string.IsNullOrWhiteSpace(options.UserAgent))
            {
                client.DefaultRequestHeaders.Add("User-Agent", options.UserAgent);
            }
        });

        return services.AddPulumiDeploymentCore();
    }

    /// <summary>
    /// Adds DeploymentKit deployment services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The configured service collection for method chaining</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddPulumiDeploymentCore();
        AddInfrastructureDomainServices(services);

        // Add health checks
        services.AddHealthChecks().AddCheck<InfrastructureHealthCheck>("infrastructure");

        return services;
    }

    private static void AddInfrastructureDomainServices(IServiceCollection services)
    {
        services.AddScoped<IContainerRegistryService, ContainerRegistryService>();
        services.AddScoped<IDatabaseService, DatabaseService>();
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddScoped<IStorageService, StorageService>();
        services.AddScoped<IBlobStorageService, BlobStorageService>();
        services.AddScoped<ICosmosDbService, CosmosDbService>();
        services.AddScoped<ITableStorageService, TableStorageService>();
        services.AddScoped<IOpenAiService, OpenAiService>();
        services.AddScoped<IContainerAppsService, ContainerAppsService>();
        services.AddScoped<IGreenBlueContainerAppsService, GreenBlueContainerAppsService>();
        services.AddScoped<ISlotManagementService, SlotManagementService>();
        services.AddScoped<IDeploymentStatusService, DeploymentStatusService>();
        services.AddScoped<IIngressManagementService, IngressManagementService>();
        services.AddScoped<ITrafficManagementService, TrafficManagementService>();
        services.AddScoped<GreenBlueTrafficSwitcher>();
        services.AddScoped<IKeyVaultService, KeyVaultService>();
        services.AddScoped<INetworkService, NetworkService>();
        services.AddScoped<IApplicationGatewayService, ApplicationGatewayService>();
        services.AddScoped<IDomainOptimizationService, DomainOptimizationService>();
        services.AddScoped<IDnsRecordService, DnsRecordService>();
        services.AddScoped<ICertificateManagementService, CertificateManagementService>();
        services.AddScoped<IEventHubsService, EventHubsService>();
        services.AddScoped<IVpnService, VpnService>();
        services.AddScoped<IFrontDoorDeployer, FrontDoorDeployer>();
        services.AddScoped<IHealthCheckService, HealthCheckService>();
        services.AddScoped<IDeploymentValidationService, DeploymentValidationService>();
        services.AddScoped<IPreDeploymentValidator, PreDeploymentValidator>();
        services.AddScoped<IStateDriftRecoveryService, StateDriftRecoveryService>();

        services.AddScoped<IAzureResourceStateValidator, AzureResourceStateValidator>();
        services.AddScoped<INamingConsistencyValidator, NamingConsistencyValidator>();
        services.AddScoped<ISubscriptionResourceGroupValidator, SubscriptionResourceGroupValidator>();
        services.AddScoped<IDriftDetectionService, DriftDetectionService>();
        services.AddScoped<IValidationOrchestratorService, ValidationOrchestratorService>();

        services.AddHttpClient<HealthCheckService>();
        services.AddHttpClient<GreenBlueHealthCheckService>();

        services.AddScoped<IInfrastructureBuilder, InfrastructureBuilder>();
        services.AddScoped<InfrastructureOrchestrator>();
    }

    /// <summary>
    /// Adds DeploymentKit deployment services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <param name="configureOptions">Configuration action for customizing service registration</param>
    /// <returns>The configured service collection for method chaining</returns>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        Action<InfrastructureServiceOptions> configureOptions)
    {
        var options = new InfrastructureServiceOptions();
        configureOptions(options);
        ValidateSdkOptions(options);

        services.AddPulumiDeploymentCore(coreOptions =>
        {
            coreOptions.EnableStructuredLogging = options.EnableStructuredLogging;
            coreOptions.EnableApplicationInsights = options.EnableApplicationInsights;
            coreOptions.HttpClientName = options.HttpClientName ?? "DeploymentKitDeployment";
            coreOptions.HttpTimeout = options.HttpTimeout;
            coreOptions.UserAgent = options.UserAgent;
            coreOptions.MaxRetryAttempts = options.MaxRetryAttempts;
        });

        AddInfrastructureDomainServices(services);
        services.AddHealthChecks().AddCheck<InfrastructureHealthCheck>("infrastructure");

        return services;
    }

    /// <summary>
    /// Adds DeploymentKit deployment services with custom logging configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureLogging">Action to configure logging</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, Action<ILoggingBuilder> configureLogging)
    {
        // Add custom logging configuration
        services.AddLogging(configureLogging);

        // Add all infrastructure services
        return services.AddInfrastructureServices();
    }

    private static void ValidateSdkOptions(InfrastructureSdkOptions options)
    {
        if (options.HttpTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.HttpTimeout), "HttpTimeout must be greater than zero.");
        }

        if (options.MaxRetryAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxRetryAttempts), "MaxRetryAttempts cannot be negative.");
        }
    }

    /// <summary>
    /// Adds validation services for infrastructure configuration
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The configured service collection for method chaining</returns>
    public static IServiceCollection AddInfrastructureValidation(this IServiceCollection services)
    {
        // Core validation services
        services.AddScoped<IDeploymentValidationService, DeploymentValidationService>();
        services.AddScoped<IPreDeploymentValidator, PreDeploymentValidator>();

        // Azure resource validation services
        services.AddScoped<IAzureResourceStateValidator, AzureResourceStateValidator>();
        services.AddScoped<IDriftDetectionService, DriftDetectionService>();

        // Naming and consistency validation
        services.AddScoped<INamingConsistencyValidator, NamingConsistencyValidator>();
        services.AddScoped<ISubscriptionResourceGroupValidator, SubscriptionResourceGroupValidator>();

        // Orchestration service
        services.AddScoped<IValidationOrchestratorService, ValidationOrchestratorService>();

        // Authentication service
        services.AddScoped<IAzureAuthenticationService, AzureAuthenticationService>();

        return services;
    }

    /// <summary>
    /// Adds health check services for infrastructure monitoring
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The configured service collection for method chaining</returns>
    public static IServiceCollection AddInfrastructureHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<InfrastructureHealthCheck>("infrastructure")
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<CacheHealthCheck>("cache")
            .AddCheck<StorageHealthCheck>("storage");

        return services;
    }

    /// <summary>
    /// Validates that the core Pulumi deployment services are registered.
    /// </summary>
    /// <param name="serviceProvider">The service provider to validate</param>
    /// <returns>True if all core services are properly registered</returns>
    public static bool ValidatePulumiDeploymentCoreServices(this IServiceProvider serviceProvider)
    {
        try
        {
            serviceProvider.GetRequiredService<IResourceNamingService>();
            serviceProvider.GetRequiredService<ICorrelationIdService>();
            serviceProvider.GetRequiredService<IResourceNameRegistry>();
            serviceProvider.GetRequiredService<IResourceNameValidator>();
            serviceProvider.GetRequiredService<ILegacyEnvironmentBridge>();
            serviceProvider.GetRequiredService<IPulumiAutomationService>();
            serviceProvider.GetRequiredService<IInfrastructureDeploymentOrchestrator>();
            serviceProvider.GetRequiredService<IDeploymentOrchestrator>();
            serviceProvider.GetRequiredService<IAzureAuthenticationService>();
            serviceProvider.GetRequiredService<IPulumiConfigLoader>();

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that all required services are registered
    /// </summary>
    /// <param name="serviceProvider">The service provider to validate</param>
    /// <returns>True if all services are properly registered</returns>
    public static bool ValidateInfrastructureServices(this IServiceProvider serviceProvider)
    {
        try
        {
            if (!serviceProvider.ValidatePulumiDeploymentCoreServices())
            {
                return false;
            }

            serviceProvider.GetRequiredService<IContainerRegistryService>();
            serviceProvider.GetRequiredService<IDatabaseService>();
            serviceProvider.GetRequiredService<ICacheService>();
            serviceProvider.GetRequiredService<IMonitoringService>();
            serviceProvider.GetRequiredService<IStorageService>();
            serviceProvider.GetRequiredService<IContainerAppsService>();
            serviceProvider.GetRequiredService<IGreenBlueContainerAppsService>();
            serviceProvider.GetRequiredService<IKeyVaultService>();
            serviceProvider.GetRequiredService<INetworkService>();
            serviceProvider.GetRequiredService<IApplicationGatewayService>();
            serviceProvider.GetRequiredService<IDomainOptimizationService>();
            serviceProvider.GetRequiredService<IFrontDoorDeployer>();
            serviceProvider.GetRequiredService<IHealthCheckService>();
            serviceProvider.GetRequiredService<IDeploymentValidationService>();
            serviceProvider.GetRequiredService<IPreDeploymentValidator>();

            // Validate validation services
            serviceProvider.GetRequiredService<IAzureResourceStateValidator>();
            serviceProvider.GetRequiredService<INamingConsistencyValidator>();
            serviceProvider.GetRequiredService<ISubscriptionResourceGroupValidator>();
            serviceProvider.GetRequiredService<IDriftDetectionService>();
            serviceProvider.GetRequiredService<IValidationOrchestratorService>();

            serviceProvider.GetRequiredService<InfrastructureOrchestrator>();

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

