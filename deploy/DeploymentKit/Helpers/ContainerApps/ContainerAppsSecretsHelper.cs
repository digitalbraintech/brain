using DeploymentKit.Constants;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using System.Text.RegularExpressions;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;

namespace DeploymentKit.Helpers.ContainerApps;

/// <summary>
/// Helper class for Container Apps secrets management.
/// </summary>
public static class ContainerAppsSecretsHelper
{
    /// <summary>
    /// Builds Container App secrets list including Key Vault secrets if ApplyToContainerApps is enabled
    /// </summary>
    /// <param name="settings">Infrastructure settings</param>
    /// <param name="containerRegistry">Container Registry outputs</param>
    /// <param name="database">Database outputs</param>
    /// <param name="keyVault">Key Vault outputs</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Tuple of secrets list and secret names</returns>
    public static (InputList<SecretArgs> secrets, List<string> secretNames) BuildSecretsListWithKeyVault(
        InfrastructureSettings settings,
        ContainerRegistryOutputs containerRegistry,
        DatabaseOutputs database,
        KeyVaultOutputs? keyVault,
        ILogger logger)
    {
        var useKeyVaultSecrets = ShouldUseKeyVaultSecrets(settings, keyVault);
        var DbSecretName = ToKeyVaultSecretName(ServiceConstants.EnvironmentVariables.PostgresConnectionString);
        var hasKeyVaultDb = useKeyVaultSecrets && settings.KeyVault?.Secrets.Keys.Any(key =>
            string.Equals(key, DbSecretName, StringComparison.OrdinalIgnoreCase)) == true;

        // Build secrets as a List first, then convert to InputList to ensure proper materialization
        var secretsList = new List<SecretArgs>();
        var secretNames = new List<string>();

        // Add standard secrets based on placeholder mode
        if (settings.Container is { UsePlaceholderImages: true })
        {
            if (!string.IsNullOrEmpty(settings.Database?.Password))
            {
                secretsList.Add(new SecretArgs
                {
                    Name = ServiceConstants.ContainerApps.DbPasswordSecretRef,
                    Value = Output.CreateSecret(settings.Database.Password)
                });
                secretNames.Add(ServiceConstants.ContainerApps.DbPasswordSecretRef);
            }

            if (!hasKeyVaultDb && settings.Database != null)
            {
                secretsList.Add(new SecretArgs
                {
                    Name = ServiceConstants.ContainerApps.PostgresConnectionStringSecretRef,
                    Value = database.ConnectionString
                });
                secretNames.Add(ServiceConstants.ContainerApps.PostgresConnectionStringSecretRef);
            }
        }
        else
        {
            secretsList.Add(new SecretArgs
            {
                Name = ServiceConstants.ContainerApps.AcrPasswordSecretRef,
                Value = containerRegistry.Password
            });
            secretNames.Add(ServiceConstants.ContainerApps.AcrPasswordSecretRef);

            if (!string.IsNullOrEmpty(settings.Database?.Password))
            {
                secretsList.Add(new SecretArgs
                {
                    Name = ServiceConstants.ContainerApps.DbPasswordSecretRef,
                    Value = Output.CreateSecret(settings.Database.Password)
                });
                secretNames.Add(ServiceConstants.ContainerApps.DbPasswordSecretRef);
            }

            if (!hasKeyVaultDb && settings.Database != null)
            {
                secretsList.Add(new SecretArgs
                {
                    Name = ServiceConstants.ContainerApps.PostgresConnectionStringSecretRef,
                    Value = database.ConnectionString
                });
                secretNames.Add(ServiceConstants.ContainerApps.PostgresConnectionStringSecretRef);
            }
        }

        // Add Key Vault secrets to Container App secrets if ApplyToContainerApps is enabled
        if (useKeyVaultSecrets)
        {
            if (settings.KeyVault != null)
            {
                logger.LogInformation("Adding {SecretCount} Key Vault secrets to Container App secrets store",
                    settings.KeyVault.Secrets.Count);

                foreach (var secret in settings.KeyVault.Secrets)
                {
                    // Sanitize secret name for Container App (lowercase, alphanumeric and hyphens only)
                    var secretName = secret.Key.ToLowerInvariant();

                    secretsList.Add(new SecretArgs
                    {
                        Name = secretName,
                        KeyVaultUrl = Output.Format($"{keyVault!.VaultUri}secrets/{secret.Key}"),
                        Identity = "System"
                    });
                    secretNames.Add(secretName);

                    logger.LogDebug("Added Container App secret {SecretName} from Key Vault", secretName);
                }
            }
        }

        logger.LogInformation("Total secrets configured for Container App: {Count}", secretsList.Count);

        // Return array directly - Pulumi has implicit conversion from T[] to InputList<T>
        return (secretsList.ToArray(), secretNames);
    }

    /// <summary>
    /// Checks if Key Vault secrets should be used for Container Apps
    /// </summary>
    /// <param name="settings">Infrastructure settings</param>
    /// <param name="keyVault">Key Vault outputs</param>
    /// <returns>True if Key Vault secrets should be used, false otherwise</returns>
    public static bool ShouldUseKeyVaultSecrets(InfrastructureSettings settings, KeyVaultOutputs? keyVault) =>
        settings.KeyVault?.ApplyToContainerApps == true &&
        keyVault != null &&
        settings.KeyVault.Secrets.Count > 0;

    /// <summary>
    /// Converts a name to a valid Key Vault secret name
    /// </summary>
    /// <param name="name">The name to convert</param>
    /// <returns>The converted Key Vault secret name</returns>
    public static string ToKeyVaultSecretName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var transformed = name.Replace('_', '-');
        transformed = Regex.Replace(transformed, @"[^a-zA-Z0-9-]", string.Empty);
        return transformed.Trim('-');
    }
}



