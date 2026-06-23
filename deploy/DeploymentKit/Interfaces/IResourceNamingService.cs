namespace DeploymentKit.Interfaces;

/// <summary>
/// Service interface for generating consistent and standardized Azure resource names
/// Ensures naming conventions are followed across all infrastructure components for better organization and management
/// </summary>
public interface IResourceNamingService
{
    /// <summary>
    /// Generates a standardized name for Azure Resource Group
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted resource group name following Azure naming conventions</returns>
    string GenerateResourceGroupName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Container Registry
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted container registry name following Azure naming conventions</returns>
    string GenerateContainerRegistryName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Database for PostgreSQL server
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted PostgreSQL server name following Azure naming conventions</returns>
    string GeneratePostgreSqlServerName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Redis Cache instance
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Redis cache name following Azure naming conventions</returns>
    string GenerateRedisCacheName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Log Analytics Workspace
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Log Analytics workspace name following Azure naming conventions</returns>
    string GenerateLogAnalyticsWorkspaceName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Application Insights instance
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Application Insights name following Azure naming conventions</returns>
    string GenerateApplicationInsightsName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Storage Account
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted storage account name following Azure naming conventions</returns>
    string GenerateStorageAccountName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for an Azure OpenAI (Cognitive Services) account
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Azure OpenAI account name following Azure naming conventions</returns>
    string GenerateOpenAiAccountName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Container Apps Environment
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Container Apps environment name following Azure naming conventions</returns>
    string GenerateContainerAppsEnvironmentName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for individual Azure Container App
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="appType">Type of application (api, jobs, web, etc.)</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Container App name following Azure naming conventions</returns>
    string GenerateContainerAppName(string prefix, string appType, string environment);

    /// <summary>
    /// Generates a standardized name for PostgreSQL database within the server
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted PostgreSQL database name following Azure naming conventions</returns>
    string GeneratePostgreSqlDatabaseName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Key Vault
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Key Vault name following Azure naming conventions</returns>
    string GenerateKeyVaultName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Virtual Network
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Virtual Network name following Azure naming conventions</returns>
    string GenerateVirtualNetworkName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Virtual Network subnet
    /// </summary>
    /// <param name="subnetType">Type of subnet (app, data, gateway, etc.)</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted subnet name following Azure naming conventions</returns>
    string GenerateSubnetName(string subnetType, string environment);

    /// <summary>
    /// Generates a standardized name for Network Security Group
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="nsgType">Type of NSG (app, data, gateway, etc.)</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Network Security Group name following Azure naming conventions</returns>
    string GenerateNetworkSecurityGroupName(string prefix, string nsgType, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Application Gateway
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Application Gateway name following Azure naming conventions</returns>
    string GenerateApplicationGatewayName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Public IP address
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Public IP name following Azure naming conventions</returns>
    string GeneratePublicIpName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Public IP address with resource type specification
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="resourceType">Type of resource using the Public IP (gateway, loadbalancer, etc.)</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Public IP name following Azure naming conventions</returns>
    string GeneratePublicIpName(string prefix, string resourceType, string environment);

    /// <summary>
    /// Generates a standardized name for Azure VPN Gateway
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted VPN Gateway name following Azure naming conventions</returns>
    string GenerateVpnGatewayName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for Azure Event Hubs Namespace
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Event Hubs namespace name following Azure naming conventions</returns>
    string GenerateEventHubsNamespaceName(string prefix, string environment);

    /// <summary>
    /// Generates a standardized name for individual Event Hub within the namespace
    /// </summary>
    /// <param name="prefix">Project or application prefix for resource identification</param>
    /// <param name="environment">Target environment (dev, staging, prod)</param>
    /// <returns>Formatted Event Hub name following Azure naming conventions</returns>
    string GenerateEventHubName(string prefix, string environment);
}

