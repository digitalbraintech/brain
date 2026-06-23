using DeploymentKit.Constants;
using DeploymentKit.Enums;
using System.ComponentModel.DataAnnotations;

namespace DeploymentKit.Settings;

public class InfrastructureSettings
{
    [Required(ErrorMessage = "Environment is required")]
    [RegularExpression(@"^(dev|development|test|staging|prod|production)$", ErrorMessage = "Environment must be dev, development, test, staging, prod, or production")]
    public string Environment { get; set; } = InfrastructureConstants.Defaults.Environment;

    [Required(ErrorMessage = "Location is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Location must be between 1 and 50 characters")]
    public string Location { get; set; } = InfrastructureConstants.Defaults.Location;

    [Required(ErrorMessage = "Naming prefix is required")]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "Naming prefix must be between 1 and 20 characters")]
    [RegularExpression(@"^[a-z][a-z0-9]*$", ErrorMessage = "Naming prefix must start with a lowercase letter and contain only lowercase letters and numbers")]
    public string NamingPrefix { get; set; } = InfrastructureConstants.Defaults.NamingPrefix;

    [Required(ErrorMessage = "Subscription ID is required")]
    [StringLength(36, MinimumLength = 36, ErrorMessage = "Subscription ID must be a valid GUID")]
    public string SubscriptionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Resource group name is required")]
    [StringLength(90, MinimumLength = 1, ErrorMessage = "Resource group name must be between 1 and 90 characters")]
    public string ResourceGroupName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Resource group type is required")]
    public ResourceGroupType ResourceGroupType { get; set; } = ResourceGroupType.Application;

    // Optional: a PostgreSQL server is only provisioned when added via AddDatabase(); null is a valid no-op.
    public DatabaseSettings? Database { get; set; } = new();

    [Required(ErrorMessage = "Container settings are required")]
    public ContainerSettings? Container { get; set; } = new();

    [Required(ErrorMessage = "Monitoring settings are required")]
    public MonitoringSettings? Monitoring { get; set; } = new();

    // Cache is optional - can use application-level caching (PostgreSQL distributed cache, IMemoryCache, etc.)
    public CacheSettings? Cache { get; set; }

    // Event Hubs is optional - only required if message broker functionality is needed
    public EventHubsSettings? EventHubs { get; set; }

    // Optional: a virtual network is only provisioned when added via AddNetworking(); null is a valid no-op.
    public NetworkSettings? Network { get; set; } = new();

    // Optional storage settings (validated when provided)
    public StorageSettings? Storage { get; set; }

    [Required(ErrorMessage = "Key Vault settings are required")]
    public KeyVaultSettings? KeyVault { get; set; } = new();

    [Required(ErrorMessage = "API settings are required")]
    public ApiSettings Api { get; set; } = new();

    // Optional green-blue deployment settings (only validated when enabled)
    public GreenBlueDeploymentSettings? GreenBlueDeployment { get; set; }

    // Optional slot settings (only used when green-blue deployment is enabled)
    public SlotSettings? GreenSlot { get; set; }

    public SlotSettings? BlueSlot { get; set; }

    // Optional Application Gateway settings (validated when provided)
    public ApplicationGatewaySettings? ApplicationGateway { get; set; }

    // Optional Blob Storage settings (validated when provided)
    public BlobStorageSettings? BlobStorage { get; set; }

    // Optional Cosmos DB settings (validated when provided)
    public CosmosDbSettings? CosmosDb { get; set; }

    // Optional Table Storage settings (validated when provided)
    public TableStorageSettings? TableStorage { get; set; }

    // Optional Azure OpenAI settings (validated when provided)
    public OpenAiSettings? OpenAi { get; set; }

    // Optional Migration settings (validated when provided)
    public MigrationSettings? Migration { get; set; }

    // Optional Custom Domain settings for domain automation (DNS, SSL, validation)
    public CustomDomainSettings? CustomDomain { get; set; }

    // Optional Azure Front Door (Standard/Premium) settings for edge routing + WAF
    public FrontDoorSettings? FrontDoor { get; set; }

    // Optional static website hosting settings
    public StaticSiteHostSettings? WebsiteStaticSite { get; set; }

    public StaticSiteHostSettings? MiniAppStaticSite { get; set; }

    // Optional Telegram bot deployment settings
    public BotContainerSettings? Bot { get; set; }

    /// <summary>
    /// Validation mode for deployment - controls the level of validation performed
    /// Full: All validation checks including Azure connectivity (default for production)
    /// Basic: Settings and naming validation only (recommended for initial deployments)
    /// Minimal: Only required fields validation (for development)
    /// Skip: No validation (not recommended)
    /// </summary>
    public ValidationMode ValidationMode { get; set; } = ValidationMode.Basic;

    /// <summary>
    /// Skip Azure authentication validation - useful when using Azure CLI authentication
    /// Default: false (authentication is validated)
    /// Set to true to skip Service Principal validation and use Azure CLI or other auth methods
    /// </summary>
    public bool SkipAzureAuthValidation { get; set; }
}



