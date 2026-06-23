using DeploymentKit.Constants;
using DeploymentKit.Interfaces;
using System.Text.RegularExpressions;

namespace DeploymentKit.Services;

/// <summary>
/// Service for generating consistent Azure resource names following Azure naming conventions
/// </summary>
public class ResourceNamingService : IResourceNamingService
{
    public string GenerateResourceGroupName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.ResourceGroup, prefix, environment);

    public string GenerateEventHubsNamespaceName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.EventHubsNamespace, prefix, environment);

    public string GenerateEventHubName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.EventHub, prefix, environment);

    public string GenerateContainerRegistryName(string prefix, string environment) => SanitizeAlphanumeric(string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.ContainerRegistry, prefix, environment), InfrastructureConstants.NamingPatterns.MaxContainerRegistryNameLength).ToLowerInvariant();

    public string GeneratePostgreSqlServerName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.PostgreSqlServer, prefix, environment);

    public string GenerateRedisCacheName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.RedisCache, prefix, environment);

    public string GenerateLogAnalyticsWorkspaceName(string prefix, string environment) => SanitizeForLogAnalytics(string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.LogAnalyticsWorkspace, prefix, environment));

    public string GenerateApplicationInsightsName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.ApplicationInsights, prefix, environment);

    public string GenerateStorageAccountName(string prefix, string environment) => SanitizeAlphanumeric( string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.StorageAccount, prefix, environment), InfrastructureConstants.NamingPatterns.MaxStorageAccountNameLength).ToLowerInvariant();

    public string GenerateOpenAiAccountName(string prefix, string environment) => SanitizeAlphanumeric(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}openai{1}", prefix, environment), 24).ToLowerInvariant();

    public string GenerateContainerAppsEnvironmentName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.ContainerAppsEnvironment, prefix, environment);

    public string GenerateContainerAppName(string prefix, string appType, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.ContainerApp, prefix, appType);

    public string GeneratePostgreSqlDatabaseName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.PostgreSqlDatabase, prefix, environment);

    public string GenerateKeyVaultName(string prefix, string environment) => SanitizeAlphanumeric( string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.KeyVault, prefix, environment), InfrastructureConstants.NamingPatterns.MaxKeyVaultNameLength).ToLowerInvariant();

    public string GenerateVirtualNetworkName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.VirtualNetwork, prefix, environment);

    public string GenerateSubnetName(string subnetType, string environment) => $"snet-{subnetType}-{environment}";

    public string GenerateNetworkSecurityGroupName(string prefix, string nsgType, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.NetworkSecurityGroup, prefix, $"{nsgType}-{environment}");

    public string GenerateApplicationGatewayName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.ApplicationGateway, prefix, environment);

    public string GeneratePublicIpName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.PublicIp, prefix, environment);

    public string GeneratePublicIpName(string prefix, string resourceType, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.PublicIp, prefix, $"{resourceType}-{environment}");

    public string GenerateVpnGatewayName(string prefix, string environment) => string.Format(System.Globalization.CultureInfo.InvariantCulture, InfrastructureConstants.NamingPatterns.VpnGateway, prefix, environment);

    /// <summary>
    /// Sanitizes a string to contain only alphanumeric characters
    /// </summary>
    private static string SanitizeAlphanumeric(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sanitized = Regex.Replace(input, @"[^a-zA-Z0-9]", "");

        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a string for Log Analytics Workspace naming (alphanumeric and hyphens only)
    /// </summary>
    private static string SanitizeForLogAnalytics(string input) => string.IsNullOrEmpty(input) ? input : Regex.Replace(input, @"[^a-zA-Z0-9-]", "");
}



