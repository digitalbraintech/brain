using DeploymentKit.Constants;
using DeploymentKit.Enums;
using DeploymentKit.Extensions;
using System.ComponentModel.DataAnnotations;

namespace DeploymentKit.Settings;

public class StorageSettings
{
    /// <summary>
    /// Storage account tier
    /// </summary>
    public StorageAccountTierType AccountTier { get; set; } = StorageAccountTierType.Standard;

    /// <summary>
    /// Storage replication type
    /// </summary>
    public StorageReplicationType ReplicationType { get; set; } = StorageReplicationType.StandardLrs;

    /// <summary>
    /// Minimum TLS version
    /// </summary>
    public TlsVersionType MinimumTlsVersion { get; set; } = TlsVersionType.Tls12;

    /// <summary>
    /// Enable HTTPS traffic only
    /// </summary>
    public bool EnableHttpsTrafficOnly { get; set; } = true;

    /// <summary>
    /// Enable blob public access
    /// </summary>
    public bool AllowBlobPublicAccess { get; set; }

    /// <summary>
    /// Enable shared key access
    /// </summary>
    public bool AllowSharedKeyAccess { get; set; } = true;

    /// <summary>
    /// When true the account's network default action is Allow (reachable from public networks); otherwise Deny.
    /// Required for workloads without VNet integration (e.g. public Container Apps) to reach the account.
    /// </summary>
    public bool AllowPublicNetworkAccess { get; set; }

    /// <summary>
    /// Default content type for blobs
    /// </summary>
    [RegularExpression(@"^[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9]/[a-zA-Z0-9][a-zA-Z0-9\-\+]*[a-zA-Z0-9]$", ErrorMessage = "Invalid content type format")]
    public string DefaultContentType { get; set; } = InfrastructureConstants.Storage.DefaultContentType;

    /// <summary>
    /// Enable versioning for blobs
    /// </summary>
    public bool EnableVersioning { get; set; }

    /// <summary>
    /// Enable change feed
    /// </summary>
    public bool EnableChangeFeed { get; set; }

    /// <summary>
    /// Enable delete retention policy
    /// </summary>
    public bool EnableDeleteRetentionPolicy { get; set; } = true;

    /// <summary>
    /// Delete retention days
    /// </summary>
    [Range(1, 365, ErrorMessage = "Delete retention days must be between 1 and 365")]
    public int DeleteRetentionDays { get; set; } = 7;

    /// <summary>
    /// Enable container delete retention policy
    /// </summary>
    public bool EnableContainerDeleteRetentionPolicy { get; set; } = true;

    /// <summary>
    /// Container delete retention days
    /// </summary>
    [Range(1, 365, ErrorMessage = "Container delete retention days must be between 1 and 365")]
    public int ContainerDeleteRetentionDays { get; set; } = 7;

    /// <summary>
    /// Enable cross-tenant replication
    /// </summary>
    public bool AllowCrossTenantReplication { get; set; }

    /// <summary>
    /// Access tier for storage account
    /// </summary>
    [RegularExpression(@"^(Hot|Cool|Archive)$", ErrorMessage = "Access tier must be Hot, Cool, or Archive")]
    public string AccessTier { get; set; } = "Hot";

    /// <summary>
    /// Whether the storage is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    // String properties for backward compatibility and Pulumi integration
    internal string AccountTierString => AccountTier.ToStringValue();
    internal string ReplicationTypeString => ReplicationType.ToStringValue();
    internal string MinimumTlsVersionString => MinimumTlsVersion.ToStringValue();
}


