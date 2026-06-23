using DeploymentKit.Components;
using DeploymentKit.Constants;
using DeploymentKit.Exceptions;
using DeploymentKit.Helpers.ContainerApps;
using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using DeploymentKit.Utilities;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;

namespace DeploymentKit.Services;

public class ContainerAppsService(ILogger<ContainerAppsService> logger, IResourceNamingService namingService, ICorrelationIdService correlationIdService) : IContainerAppsService
{
    private readonly ILogger<ContainerAppsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IResourceNamingService _namingService = namingService ?? throw new ArgumentNullException(nameof(namingService));
    private readonly ICorrelationIdService _correlationIdService = correlationIdService ?? throw new ArgumentNullException(nameof(correlationIdService));
    public Task<ContainerAppsOutputs> CreateAsync(
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
        Input<string>? azureFrontDoorId = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceGroup);

        var componentName = $"{settings.NamingPrefix}-app-runtime-{settings.Environment}";
        return DeploymentKitApp.CreateContainerAppsAsync(
            componentName,
            () => CreateCoreAsync(
                settings,
                resourceGroup,
                monitoring,
                containerRegistry,
                database,
                cache,
                network,
                eventHubs,
                storage,
                openAi,
                keyVault,
                cancellationToken,
                azureFrontDoorId));
    }

    private Task<ContainerAppsOutputs> CreateCoreAsync(
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
        Input<string>? azureFrontDoorId = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceGroup);
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(containerRegistry);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(eventHubs);

        if (string.IsNullOrEmpty(settings.NamingPrefix))
            throw new ArgumentException(ContainerAppConstants.NamingPrefixRequired);

        if (string.IsNullOrEmpty(settings.Environment))
            throw new ArgumentException(ContainerAppConstants.EnvironmentRequired);

        if (settings.Container == null)
        {
            _logger.LogInformation(ContainerAppConstants.SettingsNotProvidedMessage);
            return Task.FromResult(new ContainerAppsOutputs
            {
                EnvironmentName = string.Empty,
                EnvironmentId = Output.Create(string.Empty),
                ApiAppName = string.Empty,
                ApiAppUrl = Output.Create(string.Empty),
                JobsAppName = string.Empty,
                JobsInternalFqdn = Output.Create(string.Empty),
                BotAppName = string.Empty,
                BotAppUrl = Output.Create(string.Empty)
            });
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation(ServiceConstants.ContainerApps.CreationStartMessage, settings.Environment);

            if (settings.Container.UsePlaceholderImages)
            {
                _logger.LogWarning(ContainerAppConstants.PlaceholderImageWarning);
            }

            if (!string.IsNullOrEmpty(settings.Network?.ContainerAppsSubnet))
            {
                var subnetAddressPrefix = settings.Network.ContainerAppsSubnet;
                var prefixLength = int.Parse(subnetAddressPrefix.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture);

                if (prefixLength > 23)
                {
                    throw new ArgumentException(string.Format(System.Globalization.CultureInfo.InvariantCulture, ContainerAppConstants.SubnetTooSmallError, subnetAddressPrefix, EnvironmentVariableNames.Network.ContainerAppsSubnetAddressSpace));
                }
            }

            var environmentName = _namingService.GenerateContainerAppsEnvironmentName(settings.NamingPrefix, settings.Environment);
            var managedEnvArgs = new ManagedEnvironmentArgs
            {
                EnvironmentName = environmentName,
                ResourceGroupName = resourceGroup,
                Location = settings.Location,
                AppLogsConfiguration = new AppLogsConfigurationArgs
                {
                    Destination = ServiceConstants.ContainerApps.LogAnalyticsDestination,
                    LogAnalyticsConfiguration = new LogAnalyticsConfigurationArgs
                    {
                        CustomerId = monitoring.LogAnalyticsWorkspaceId,
                        SharedKey = monitoring.LogAnalyticsWorkspacePrimaryKey
                    }
                },
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.ContainerAppsEnvironmentType)
            };

            if (!string.IsNullOrEmpty(settings.Network?.ContainerAppsSubnet))
            {
                managedEnvArgs.VnetConfiguration = new VnetConfigurationArgs
                {
                    InfrastructureSubnetId = network.ContainerAppsSubnetId,
                    Internal = settings.Network.IsInternalEnvironment
                };
            }

            var containerAppsEnvironment = new ManagedEnvironment(environmentName, managedEnvArgs, ComponentResourceScope.CreateChildOptions(environmentName));

            var digitalBrainEnv = BuildDigitalBrainRuntimeEnv(settings, storage, openAi);

            var apiApp = CreateApiContainerApp(settings, resourceGroup, containerAppsEnvironment, containerRegistry, database, cache, monitoring, eventHubs, keyVault, azureFrontDoorId, digitalBrainEnv);
            var jobsApp = CreateJobsContainerApp(settings, resourceGroup, containerAppsEnvironment, containerRegistry, database, cache, monitoring, eventHubs, keyVault, azureFrontDoorId, digitalBrainEnv);
            var botApp = CreateBotContainerApp(settings, resourceGroup, containerAppsEnvironment, containerRegistry, database, cache, monitoring, eventHubs, keyVault, azureFrontDoorId, digitalBrainEnv);

            ContainerAppsIdentityHelper.ConfigureKeyVaultAccessForContainerApps(settings, keyVault, apiApp, jobsApp, botApp, _namingService);

            var outputs = new ContainerAppsOutputs
            {
                EnvironmentName = environmentName,
                EnvironmentId = containerAppsEnvironment.Id,
                ApiAppName = _namingService.GenerateContainerAppName(settings.NamingPrefix, ServiceConstants.ContainerAppTypes.Api, settings.Environment),
                ApiAppUrl = ResolveContainerAppUrl(apiApp),
                JobsAppName = _namingService.GenerateContainerAppName(settings.NamingPrefix, ServiceConstants.ContainerAppTypes.Jobs, settings.Environment),
                JobsInternalFqdn = jobsApp.Configuration.Apply(c => c?.Ingress?.Fqdn ?? string.Empty),
                BotAppName = botApp is null ? string.Empty : _namingService.GenerateContainerAppName(settings.NamingPrefix, ServiceConstants.ContainerAppTypes.Bot, settings.Environment),
                BotAppUrl = botApp is null ? Output.Create(string.Empty) : ResolveContainerAppUrl(botApp),
                Environment = containerAppsEnvironment,
                ApiApp = apiApp,
                JobsApp = jobsApp,
                BotApp = botApp
            };

            _logger.LogInformation(ServiceConstants.ContainerApps.CreationSuccessMessage, environmentName);
            return Task.FromResult(outputs);
        }
        catch (Exception ex)
        {
            var correlationId = _correlationIdService.GetOrGenerateCorrelationId();
            _logger.LogError(ex, ServiceConstants.ContainerApps.CreationFailedMessage, settings.Environment);
            throw new ResourceCreationException(
                $"Failed to create Container Apps for environment: {settings.Environment}",
                ex,
                ServiceConstants.ResourceTypes.ContainerApps,
                "ManagedEnvironment/ContainerApp",
                settings.Environment,
                correlationId,
                ServiceConstants.ErrorCodes.ContainerAppsCreationFailed);
        }
    }

    public async Task<ContainerAppsOutputs> CreateAsync(
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
        Input<string>? azureFrontDoorId = null)
    {
        ArgumentNullException.ThrowIfNull(monitoringTask);
        ArgumentNullException.ThrowIfNull(containerRegistryTask);
        ArgumentNullException.ThrowIfNull(databaseTask);
        ArgumentNullException.ThrowIfNull(cacheTask);
        ArgumentNullException.ThrowIfNull(networkTask);
        ArgumentNullException.ThrowIfNull(eventHubsTask);
        ArgumentNullException.ThrowIfNull(keyVaultTask);

        cancellationToken.ThrowIfCancellationRequested();
        await Task.WhenAll(monitoringTask, containerRegistryTask, databaseTask, cacheTask, networkTask, eventHubsTask, keyVaultTask);

        return await CreateAsync(
            settings,
            resourceGroup,
            await monitoringTask,
            await containerRegistryTask,
            await databaseTask,
            await cacheTask,
            await networkTask,
            await eventHubsTask,
            keyVault: await keyVaultTask,
            cancellationToken: cancellationToken,
            azureFrontDoorId: azureFrontDoorId);
    }

    private ContainerApp CreateApiContainerApp(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        ManagedEnvironment environment,
        ContainerRegistryOutputs containerRegistry,
        DatabaseOutputs database,
        CacheOutputs cache,
        MonitoringOutputs monitoring,
        EventHubsOutputs eventHubs,
        KeyVaultOutputs? keyVault,
        Input<string>? azureFrontDoorId,
        IEnumerable<EnvironmentVarArgs>? additionalEnv)
    {
        var apiAppName = _namingService.GenerateContainerAppName(settings.NamingPrefix, ServiceConstants.ContainerAppTypes.Api, settings.Environment);
        var imageToUse = ResolveImage(settings.Container!.UsePlaceholderImages, containerRegistry.LoginServer, settings.Container.ApiImageTag);
        var ingressConfig = ContainerAppsConfigurationHelper.CreateIngressConfiguration(settings.Container.IngressSettings, _logger);
        var registries = BuildRegistries(settings, containerRegistry);
        var (secrets, secretNames) = ContainerAppsSecretsHelper.BuildSecretsListWithKeyVault(settings, containerRegistry, database, keyVault, _logger);

        _logger.LogInformation("Creating API Container App with {SecretCount} secrets configured", secretNames.Count);

        return new ContainerApp(apiAppName, new ContainerAppArgs
        {
            ContainerAppName = apiAppName,
            ResourceGroupName = resourceGroup,
            Location = settings.Location,
            ManagedEnvironmentId = environment.Id,
            Identity = ContainerAppsIdentityHelper.GetContainerAppIdentity(settings, keyVault),
            Configuration = new ConfigurationArgs
            {
                Ingress = ingressConfig,
                Registries = registries,
                Secrets = secrets
            },
            Template = new TemplateArgs
            {
                Containers =
                [
                    new ContainerArgs
                    {
                        Name = ServiceConstants.ContainerAppTypes.Api,
                        Image = imageToUse,
                        Env = ContainerAppsConfigurationHelper.BuildEnvironmentVariables(settings, cache, eventHubs, monitoring, keyVault, azureFrontDoorId, _logger, additionalEnv),
                        Resources = new ContainerResourcesArgs
                        {
                            Cpu = settings.Container.CpuLimit,
                            Memory = $"{settings.Container.MemoryLimit}{ServiceConstants.ContainerApps.MemoryUnit}"
                        }
                    }
                ],
                Scale = ContainerAppsConfigurationHelper.CreateScaleRules(settings)
            },
            Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.ContainerAppApiType)
        }, ComponentResourceScope.CreateChildOptions(apiAppName));
    }

    private ContainerApp CreateJobsContainerApp(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        ManagedEnvironment environment,
        ContainerRegistryOutputs containerRegistry,
        DatabaseOutputs database,
        CacheOutputs cache,
        MonitoringOutputs monitoring,
        EventHubsOutputs eventHubs,
        KeyVaultOutputs? keyVault,
        Input<string>? azureFrontDoorId,
        IEnumerable<EnvironmentVarArgs>? additionalEnv)
    {
        var jobsAppName = _namingService.GenerateContainerAppName(settings.NamingPrefix, ServiceConstants.ContainerAppTypes.Jobs, settings.Environment);
        var imageToUse = ResolveImage(settings.Container!.UsePlaceholderImages, containerRegistry.LoginServer, settings.Container.JobsImageTag);
        // The Jobs app is a background worker (e.g. an Orleans silo) that serves no HTTP, so it gets no ingress —
        // an HTTP readiness probe on an ingress port it never listens on would keep the revision unhealthy.
        IngressArgs? ingressConfig = null;
        var registries = BuildRegistries(settings, containerRegistry);
        var (secrets, secretNames) = ContainerAppsSecretsHelper.BuildSecretsListWithKeyVault(settings, containerRegistry, database, keyVault, _logger);

        _logger.LogInformation("Creating Jobs Container App with {SecretCount} secrets configured", secretNames.Count);

        return new ContainerApp(jobsAppName, new ContainerAppArgs
        {
            ContainerAppName = jobsAppName,
            ResourceGroupName = resourceGroup,
            Location = settings.Location,
            ManagedEnvironmentId = environment.Id,
            Identity = ContainerAppsIdentityHelper.GetContainerAppIdentity(settings, keyVault),
            Configuration = new ConfigurationArgs
            {
                Ingress = ingressConfig,
                Registries = registries,
                Secrets = secrets
            },
            Template = new TemplateArgs
            {
                Containers =
                [
                    new ContainerArgs
                    {
                        Name = ServiceConstants.ContainerAppTypes.Jobs,
                        Image = imageToUse,
                        Env = ContainerAppsConfigurationHelper.BuildEnvironmentVariables(settings, cache, eventHubs, monitoring, keyVault, azureFrontDoorId, _logger, additionalEnv),
                        Resources = new ContainerResourcesArgs
                        {
                            Cpu = settings.Container.CpuLimit,
                            Memory = $"{settings.Container.MemoryLimit}{ServiceConstants.ContainerApps.MemoryUnit}"
                        }
                    }
                ],
                Scale = ContainerAppsConfigurationHelper.CreateScaleRules(settings)
            },
            Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.ContainerAppJobsType)
        }, ComponentResourceScope.CreateChildOptions(jobsAppName));
    }

    private ContainerApp? CreateBotContainerApp(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        ManagedEnvironment environment,
        ContainerRegistryOutputs containerRegistry,
        DatabaseOutputs database,
        CacheOutputs cache,
        MonitoringOutputs monitoring,
        EventHubsOutputs eventHubs,
        KeyVaultOutputs? keyVault,
        Input<string>? azureFrontDoorId,
        IEnumerable<EnvironmentVarArgs>? additionalEnv)
    {
        if (settings.Bot?.Enabled != true)
        {
            return null;
        }

        var botAppName = _namingService.GenerateContainerAppName(settings.NamingPrefix, ServiceConstants.ContainerAppTypes.Bot, settings.Environment);
        var imageTag = string.IsNullOrWhiteSpace(settings.Bot.ImageTag)
            ? settings.Container?.BotImageTag
            : settings.Bot.ImageTag;
        var imageToUse = ResolveImage(settings.Container!.UsePlaceholderImages, containerRegistry.LoginServer, imageTag ?? string.Empty);
        var ingressConfig = ContainerAppsConfigurationHelper.CreateIngressConfiguration(CreateBotIngressSettings(settings), _logger);
        var registries = BuildRegistries(settings, containerRegistry);
        var (secrets, secretNames) = ContainerAppsSecretsHelper.BuildSecretsListWithKeyVault(settings, containerRegistry, database, keyVault, _logger);

        _logger.LogInformation("Creating Bot Container App with {SecretCount} secrets configured", secretNames.Count);

        return new ContainerApp(botAppName, new ContainerAppArgs
        {
            ContainerAppName = botAppName,
            ResourceGroupName = resourceGroup,
            Location = settings.Location,
            ManagedEnvironmentId = environment.Id,
            Identity = ContainerAppsIdentityHelper.GetContainerAppIdentity(settings, keyVault),
            Configuration = new ConfigurationArgs
            {
                Ingress = ingressConfig,
                Registries = registries,
                Secrets = secrets
            },
            Template = new TemplateArgs
            {
                Containers =
                [
                    new ContainerArgs
                    {
                        Name = ServiceConstants.ContainerAppTypes.Bot,
                        Image = imageToUse,
                        Env = BuildBotEnvironmentVariables(settings, cache, eventHubs, monitoring, keyVault, azureFrontDoorId, additionalEnv),
                        Resources = new ContainerResourcesArgs
                        {
                            Cpu = settings.Container.CpuLimit,
                            Memory = $"{settings.Container.MemoryLimit}{ServiceConstants.ContainerApps.MemoryUnit}"
                        }
                    }
                ],
                Scale = ContainerAppsConfigurationHelper.CreateScaleRules(settings)
            },
            Tags = ResourceTagHelper.GetStandardTags(settings.Environment, "container-app-bot")
        }, ComponentResourceScope.CreateChildOptions(botAppName));
    }

    private InputList<EnvironmentVarArgs> BuildBotEnvironmentVariables(
        InfrastructureSettings settings,
        CacheOutputs cache,
        EventHubsOutputs eventHubs,
        MonitoringOutputs monitoring,
        KeyVaultOutputs? keyVault,
        Input<string>? azureFrontDoorId,
        IEnumerable<EnvironmentVarArgs>? additionalEnv)
    {
        var botSettings = settings.Bot!;
        var kafkaBootstrap = eventHubs.EventHubsEndpoint.Apply(endpoint =>
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return string.Empty;
            }

            var normalized = endpoint.Replace("sb://", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');
            return $"{normalized}:9093";
        });

        var additionalEnvironmentVariables = new List<EnvironmentVarArgs>
        {
            new() { Name = ServiceConstants.EnvironmentVariables.BotWebhookUrl, Value = botSettings.WebhookUrl },
            new() { Name = ServiceConstants.EnvironmentVariables.BotWebhookSecretToken, Value = botSettings.WebhookSecretToken },
            new() { Name = ServiceConstants.EnvironmentVariables.BotMiniAppUrl, Value = botSettings.MiniAppUrl },
            new() { Name = ServiceConstants.EnvironmentVariables.KafkaConnectionString, Value = kafkaBootstrap },
            new() { Name = ServiceConstants.EnvironmentVariables.KafkaSecurityProtocol, Value = "SaslSsl" },
            new() { Name = ServiceConstants.EnvironmentVariables.KafkaSaslMechanism, Value = "Plain" },
            new() { Name = ServiceConstants.EnvironmentVariables.KafkaSaslUsername, Value = "$ConnectionString" },
            new() { Name = ServiceConstants.EnvironmentVariables.KafkaSaslPassword, Value = eventHubs.EventHubsConnectionString }
        };

        if (additionalEnv != null)
        {
            additionalEnvironmentVariables.AddRange(additionalEnv);
        }

        return ContainerAppsConfigurationHelper.BuildEnvironmentVariables(
            settings,
            cache,
            eventHubs,
            monitoring,
            keyVault,
            azureFrontDoorId,
            _logger,
            additionalEnvironmentVariables);
    }

    // Plain env vars from the caller (e.g. DigitalBrain__Llm__Provider/Model, DIGITALBRAIN_ENV) plus the
    // NeuroOS silo/gateway runtime contract: one Storage connection string drives Orleans clustering (Table)
    // and grain/journal storage (Blob); the Azure OpenAI endpoint+key back the cloud IChatClient.
    private static List<EnvironmentVarArgs> BuildDigitalBrainRuntimeEnv(InfrastructureSettings settings, StorageOutputs? storage, OpenAiOutputs? openAi)
    {
        var env = new List<EnvironmentVarArgs>();

        if (settings.Container?.AdditionalEnvironmentVariables != null)
        {
            foreach (var pair in settings.Container.AdditionalEnvironmentVariables)
            {
                env.Add(new EnvironmentVarArgs { Name = pair.Key, Value = pair.Value });
            }
        }

        if (storage != null)
        {
            env.Add(new EnvironmentVarArgs { Name = "ConnectionStrings__clustering", Value = storage.ConnectionString });
            env.Add(new EnvironmentVarArgs { Name = "ConnectionStrings__grainstate", Value = storage.ConnectionString });
            env.Add(new EnvironmentVarArgs { Name = "ConnectionStrings__journal", Value = storage.ConnectionString });
        }

        if (openAi != null)
        {
            env.Add(new EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIEndpoint", Value = openAi.Endpoint });
            env.Add(new EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIKey", Value = openAi.PrimaryKey });
        }

        return env;
    }

    private static Output<string> ResolveContainerAppUrl(ContainerApp app) =>
        app.Configuration.Apply(c => string.IsNullOrWhiteSpace(c?.Ingress?.Fqdn) ? string.Empty : $"https://{c.Ingress.Fqdn}");

    private static Input<string> ResolveImage(bool usePlaceholderImages, Output<string> loginServer, string imageTag) =>
        usePlaceholderImages || string.IsNullOrWhiteSpace(imageTag)
            ? Output.Create(ContainerAppConstants.PlaceholderImage)
            : Output.Format($"{loginServer}/{imageTag}");

    private static InputList<RegistryCredentialsArgs> BuildRegistries(InfrastructureSettings settings, ContainerRegistryOutputs containerRegistry)
    {
        if (settings.Container?.UsePlaceholderImages == true)
        {
            return [];
        }

        return
        [
            new RegistryCredentialsArgs
            {
                Server = containerRegistry.LoginServer,
                Username = containerRegistry.Username,
                PasswordSecretRef = ServiceConstants.ContainerApps.AcrPasswordSecretRef
            }
        ];
    }

    private static IngressSettings CreateInternalIngressSettings(InfrastructureSettings settings) => new()
    {
        External = false,
        TargetPort = settings.Container?.IngressSettings?.TargetPort ?? ServiceConstants.ContainerDefaults.DefaultTargetPort,
        AllowInsecure = false,
        Transport = settings.Container?.IngressSettings?.Transport ?? ServiceConstants.ContainerDefaults.DefaultTransport
    };

    private static IngressSettings CreateBotIngressSettings(InfrastructureSettings settings) => new()
    {
        External = settings.Bot?.ExternalIngress ?? true,
        TargetPort = settings.Bot?.TargetPort ?? settings.Container?.IngressSettings?.TargetPort ?? ServiceConstants.ContainerDefaults.DefaultTargetPort,
        AllowInsecure = false,
        Transport = settings.Container?.IngressSettings?.Transport ?? ServiceConstants.ContainerDefaults.DefaultTransport
    };

    async Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken)
    {
        _logger.LogWarning(ContainerAppConstants.MinimalDependenciesWarning);

        var monitoring = new MonitoringOutputs
        {
            LogAnalyticsWorkspaceId = Output.Create(ServiceConstants.TestDefaults.WorkspaceId),
            LogAnalyticsWorkspacePrimaryKey = Output.CreateSecret(ServiceConstants.TestDefaults.WorkspaceKey),
            ApplicationInsightsInstrumentationKey = Output.Create(ServiceConstants.TestDefaults.AIKey),
            ApplicationInsightsConnectionString = Output.Create(ServiceConstants.TestDefaults.AIConnection)
        };

        var containerRegistry = new ContainerRegistryOutputs
        {
            LoginServer = Output.Create(ContainerAppConstants.DefaultRegistryServer),
            Username = Output.Create(""),
            Password = Output.CreateSecret(string.Empty),
            ResourceId = Output.Create(string.Empty)
        };

        var database = new DatabaseOutputs
        {
            ConnectionString = Output.CreateSecret(ServiceConstants.TestDefaults.ConnectionString),
            ServerName = ServiceConstants.TestDefaults.ServerName,
            DatabaseName = ServiceConstants.TestDefaults.DatabaseName
        };

        var cache = new CacheOutputs
        {
            ConnectionString = Output.CreateSecret(ServiceConstants.TestDefaults.RedisConnection),
            HostName = Output.Create(ServiceConstants.TestDefaults.RedisHost),
            Name = ServiceConstants.TestDefaults.RedisName
        };

        var network = new NetworkOutputs
        {
            VirtualNetworkId = Output.Create(ServiceConstants.TestDefaults.VnetId),
            ContainerAppsSubnetId = Output.Create(ServiceConstants.TestDefaults.SubnetId),
            ApplicationGatewaySubnetId = Output.Create(ServiceConstants.TestDefaults.AppGatewaySubnetId)
        };

        var eventHubs = new EventHubsOutputs
        {
            EventHubsNamespaceName = Output.Create(ServiceConstants.TestDefaults.EventHubNamespace),
            EventHubsConnectionString = Output.CreateSecret(ServiceConstants.TestDefaults.EventHubConnection),
            EventHubsEndpoint = Output.Create("sb://localhost/"),
            EventHubsResourceId = Output.Create(string.Empty),
            EventHubName = "DeploymentKit",
            ConsumerGroupName = "$Default"
        };

        var result = await CreateAsync(settings, resourceGroup, monitoring, containerRegistry, database, cache, network, eventHubs, keyVault: null, cancellationToken: cancellationToken, azureFrontDoorId: null);
        return result;
    }

    Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup) =>
        ((IInfrastructureService)this).CreateAsync(settings, resourceGroup, CancellationToken.None);
}
