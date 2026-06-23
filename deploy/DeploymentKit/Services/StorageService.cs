using DeploymentKit.Constants;
using DeploymentKit.Exceptions;
using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using DeploymentKit.Utilities;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DeploymentKit.Services;

/// <summary>
/// Service for managing Azure Storage Account resources
/// </summary>
public class StorageService(ILogger<StorageService> logger, IResourceNamingService namingService, ICorrelationIdService correlationIdService) : IStorageService
{
    private readonly ILogger<StorageService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IResourceNamingService _namingService = namingService ?? throw new ArgumentNullException(nameof(namingService));
    private readonly ICorrelationIdService _correlationIdService = correlationIdService ?? throw new ArgumentNullException(nameof(correlationIdService));

    public Task<StorageOutputs> CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceGroup);

        if (string.IsNullOrEmpty(settings.NamingPrefix))
            throw new ArgumentException("NamingPrefix cannot be null or empty");

        if (string.IsNullOrEmpty(settings.Environment))
            throw new ArgumentException("Environment cannot be null or empty");

        // If Storage settings are not provided, skip Storage Account provisioning
        if (settings.Storage == null)
        {
            _logger.LogInformation("Storage settings not provided. Skipping Storage Account provisioning.");
            return Task.FromResult(new StorageOutputs
            {
                Name = string.Empty,
                AccountName = Output.Create(string.Empty),
                PrimaryKey = Output.CreateSecret(string.Empty),
                ConnectionString = Output.CreateSecret(string.Empty),
                WebsiteAccountName = Output.Create(string.Empty),
                WebsitePrimaryEndpoint = Output.Create(string.Empty),
                MiniAppAccountName = Output.Create(string.Empty),
                MiniAppPrimaryEndpoint = Output.Create(string.Empty)
            });
        }

        // Use correlation ID from service instead of generating new one
        var correlationId = _correlationIdService.GetOrGenerateCorrelationId();
        var stopwatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [LoggingConstants.PropertyNames.CorrelationId] = correlationId,
            [LoggingConstants.PropertyNames.Environment] = settings.Environment,
            [LoggingConstants.PropertyNames.Service] = LoggingConstants.ServiceNames.StorageService,
            [LoggingConstants.PropertyNames.Operation] = ServiceConstants.ServiceOperations.CreateStorageAccount
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(ServiceConstants.Storage.CreationStartMessage,
                settings.Environment, correlationId, Kind.StorageV2, settings.Storage.ReplicationTypeString, settings.Storage.AccessTier, settings.Storage.EnableHttpsTrafficOnly);

            var storageAccountName = _namingService.GenerateStorageAccountName(settings.NamingPrefix, settings.Environment);

            _logger.LogDebug(ServiceConstants.Storage.AccountNameGeneratedMessage,
                correlationId, storageAccountName);

            // Create Storage Account
            var storageStopwatch = Stopwatch.StartNew();
            var networkDefaultAction = settings.Storage.AllowPublicNetworkAccess ? DefaultAction.Allow : DefaultAction.Deny;
            var storageAccount = CreateStorageAccount(settings, resourceGroup, storageAccountName, correlationId, networkDefaultAction, enableStaticWebsite: false, siteSettings: null);
            _logger.LogInformation(ServiceConstants.Storage.AccountCreationSuccessMessage, storageAccountName, storageStopwatch.ElapsedMilliseconds, correlationId);

            var websiteStorage = CreateStaticSiteStorageAccount(settings, resourceGroup, correlationId, settings.WebsiteStaticSite);
            var miniAppStorage = CreateStaticSiteStorageAccount(settings, resourceGroup, correlationId, settings.MiniAppStaticSite);

            // Get Storage Account Keys
            var keysStopwatch = Stopwatch.StartNew();
            var storageAccountKeys = GetStorageAccountKeys(resourceGroup, storageAccount, correlationId);
            _logger.LogInformation(ServiceConstants.Storage.KeysRetrievalSuccessMessage, keysStopwatch.ElapsedMilliseconds, correlationId);

            // Generate connection string
            var primaryKey = Output.CreateSecret(storageAccountKeys.Apply(k => k.Keys[0].Value));
            var connectionString = Output.CreateSecret(
                Output.Tuple(storageAccount.Name, primaryKey)
                    .Apply(t => string.Format(System.Globalization.CultureInfo.InvariantCulture, DeploymentConstants.Storage.ConnectionStringTemplate, t.Item1, t.Item2)));

            var outputs = new StorageOutputs
            {
                Name = storageAccountName,
                AccountName = storageAccount.Name,
                PrimaryKey = primaryKey,
                ConnectionString = connectionString,
                WebsiteAccountName = websiteStorage?.Name ?? Output.Create(string.Empty),
                WebsitePrimaryEndpoint = websiteStorage?.PrimaryEndpoints.Apply(endpoints => endpoints.Web ?? endpoints.Blob ?? string.Empty) ?? Output.Create(string.Empty),
                MiniAppAccountName = miniAppStorage?.Name ?? Output.Create(string.Empty),
                MiniAppPrimaryEndpoint = miniAppStorage?.PrimaryEndpoints.Apply(endpoints => endpoints.Web ?? endpoints.Blob ?? string.Empty) ?? Output.Create(string.Empty)
            };

            stopwatch.Stop();
            _logger.LogInformation(ServiceConstants.Storage.CreationSuccessMessage, settings.Environment, stopwatch.ElapsedMilliseconds, correlationId, storageAccountName, Kind.StorageV2, settings.Storage.ReplicationTypeString);

            return Task.FromResult(outputs);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(ServiceConstants.Storage.CreationCancelledMessage, settings.Environment, stopwatch.ElapsedMilliseconds, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, ServiceConstants.Storage.CreationFailedMessage, settings.Environment, stopwatch.ElapsedMilliseconds, correlationId, ex.Message);

            throw new ResourceCreationException(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, ServiceConstants.Storage.ResourceCreationFailedMessage, settings.Environment, correlationId),
                ex,
                ServiceConstants.ResourceTypes.Storage,
                ServiceConstants.ResourceTypes.StorageAccount,
                settings.Environment,
                correlationId,
                ServiceConstants.ErrorCodes.StorageCreationFailed);
        }
    }

    private StorageAccount CreateStorageAccount(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        string storageAccountName,
        string correlationId,
        DefaultAction defaultAction,
        bool enableStaticWebsite,
        StaticSiteHostSettings? siteSettings)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [LoggingConstants.PropertyNames.ResourceType] = ServiceConstants.ResourceTypes.StorageAccount,
            [LoggingConstants.PropertyNames.StorageAccountName] = storageAccountName,
            [LoggingConstants.PropertyNames.CorrelationId] = correlationId
        });

        try
        {
            _logger.LogInformation(ServiceConstants.Storage.AccountCreationStartMessage, storageAccountName, settings.Location, correlationId, Kind.StorageV2, settings.Storage.ReplicationTypeString, AccessTier.Hot, settings.Storage.MinimumTlsVersionString);

            var storageAccount = new StorageAccount(storageAccountName, new StorageAccountArgs
            {
                AccountName = storageAccountName,
                ResourceGroupName = resourceGroup,
                Location = settings.Location,
                Kind = Kind.StorageV2,
                Sku = new SkuArgs
                {
                    Name = settings.Storage.ReplicationTypeString
                },
                AccessTier = settings.Storage.AccessTier switch
                {
                    "Hot" => AccessTier.Hot,
                    "Cool" => AccessTier.Cool,
                    _ => AccessTier.Hot
                },
                AllowBlobPublicAccess = settings.Storage.AllowBlobPublicAccess,
                EnableHttpsTrafficOnly = settings.Storage.EnableHttpsTrafficOnly,
                MinimumTlsVersion = settings.Storage.MinimumTlsVersionString,
                AllowSharedKeyAccess = settings.Storage.AllowSharedKeyAccess,
                NetworkRuleSet = new NetworkRuleSetArgs
                {
                    DefaultAction = defaultAction
                },
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.StorageAccountType)
            });


            _logger.LogDebug(ServiceConstants.Storage.AccountConfiguredMessage, storageAccountName, correlationId);

            return storageAccount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ServiceConstants.Storage.AccountCreationFailedMessage,
                storageAccountName, correlationId, ex.Message);
            throw;
        }
    }

    private StorageAccount? CreateStaticSiteStorageAccount(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        string correlationId,
        StaticSiteHostSettings? siteSettings)
    {
        if (siteSettings?.Enabled != true)
        {
            return null;
        }

        var siteSuffix = string.IsNullOrWhiteSpace(siteSettings.SiteName) ? "site" : siteSettings.SiteName;
        var storageAccountName = BuildStaticSiteStorageAccountName(settings.NamingPrefix, siteSuffix, settings.Environment);
        return CreateStorageAccount(settings, resourceGroup, storageAccountName, correlationId, DefaultAction.Allow, enableStaticWebsite: true, siteSettings);
    }

    private static string BuildStaticSiteStorageAccountName(string prefix, string siteName, string environment)
    {
        var combined = $"{prefix}{siteName}{environment}";
        var sanitized = Regex.Replace(combined, "[^a-zA-Z0-9]", string.Empty).ToLowerInvariant();
        return sanitized.Length <= 24 ? sanitized : sanitized[..24];
    }

    private Output<ListStorageAccountKeysResult> GetStorageAccountKeys(Input<string> resourceGroup, StorageAccount storageAccount, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [LoggingConstants.PropertyNames.ResourceType] = ServiceConstants.ResourceTypes.StorageAccountKeys,
            [LoggingConstants.PropertyNames.CorrelationId] = correlationId
        });

        try
        {
            _logger.LogInformation(ServiceConstants.Storage.KeysRetrievalStartMessage, correlationId);

            var storageAccountKeys = ListStorageAccountKeys.Invoke(new ListStorageAccountKeysInvokeArgs
            {
                ResourceGroupName = resourceGroup,
                AccountName = storageAccount.Name
            });

            _logger.LogDebug(ServiceConstants.Storage.KeysConfiguredMessage, correlationId);

            return storageAccountKeys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ServiceConstants.Storage.KeysRetrievalFailedMessage, correlationId, ex.Message);
            throw;
        }
    }

    async Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken) => await CreateAsync(settings, resourceGroup, cancellationToken);

    async Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup) => await CreateAsync(settings, resourceGroup);
}



