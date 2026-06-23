using DeploymentKit.Constants;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;

namespace DeploymentKit.Helpers.ContainerApps;

/// <summary>
/// Helper class for managing Container App environment variables.
/// </summary>
public static class ContainerAppEnvVarHelper
{
    /// <summary>
    /// Builds the list of environment variables for a Container App.
    /// </summary>
    public static InputList<EnvironmentVarArgs> BuildEnvironmentVariables(
        InfrastructureSettings settings,
        CacheOutputs cache,
        EventHubsOutputs eventHubs,
        MonitoringOutputs monitoring,
        KeyVaultOutputs? keyVault,
        Input<string>? azureFrontDoorId,
        ILogger logger,
        IEnumerable<EnvironmentVarArgs>? additionalEnvironmentVariables = null)
    {
        var useKeyVaultSecrets = ContainerAppsSecretsHelper.ShouldUseKeyVaultSecrets(settings, keyVault);
        var DbSecretName = ContainerAppsSecretsHelper.ToKeyVaultSecretName(ServiceConstants.EnvironmentVariables.PostgresConnectionString);
        var hasKeyVaultDb = useKeyVaultSecrets && settings.KeyVault?.Secrets.Keys.Any(key =>
            string.Equals(key, DbSecretName, StringComparison.OrdinalIgnoreCase)) == true;

        var envVarsList = new List<EnvironmentVarArgs>
        {
            new() { Name = ServiceConstants.EnvironmentVariables.AspNetCoreUrls, Value = ServiceConstants.ContainerDefaults.AspNetCoreUrlsValue },
            new() { Name = ServiceConstants.EnvironmentVariables.AspNetCoreEnvironment, Value = settings.Environment },
            new() { Name = ServiceConstants.EnvironmentVariables.RedisConnectionString, Value = cache.ConnectionString },
            new() { Name = ServiceConstants.EnvironmentVariables.EventHubsConnectionString, Value = eventHubs.EventHubsConnectionString },
            new() { Name = ServiceConstants.EnvironmentVariables.ApplicationInsightsConnectionString, Value = monitoring.ApplicationInsightsConnectionString },
            new() { Name = ServiceConstants.EnvironmentVariables.OtelExporterEndpoint, Value = ServiceConstants.ContainerDefaults.OtelExporterEndpointValue }
        };

        var envVarNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ServiceConstants.EnvironmentVariables.AspNetCoreUrls,
            ServiceConstants.EnvironmentVariables.AspNetCoreEnvironment,
            ServiceConstants.EnvironmentVariables.PostgresConnectionString,
            ServiceConstants.EnvironmentVariables.RedisConnectionString,
            ServiceConstants.EnvironmentVariables.EventHubsConnectionString,
            ServiceConstants.EnvironmentVariables.ApplicationInsightsConnectionString,
            ServiceConstants.EnvironmentVariables.OtelExporterEndpoint
        };

        // Only wire the Postgres connection string when a database is actually provisioned; an empty
        // secret value is rejected by Container Apps (ContainerAppSecretInvalid).
        if (hasKeyVaultDb)
        {
            envVarsList.Add(new EnvironmentVarArgs
            {
                Name = ServiceConstants.EnvironmentVariables.PostgresConnectionString,
                SecretRef = DbSecretName.ToLowerInvariant()
            });
        }
        else if (settings.Database != null)
        {
            envVarsList.Add(new EnvironmentVarArgs
            {
                Name = ServiceConstants.EnvironmentVariables.PostgresConnectionString,
                SecretRef = ServiceConstants.ContainerApps.PostgresConnectionStringSecretRef
            });
        }

        if (azureFrontDoorId != null)
        {
            envVarNames.Add(ServiceConstants.EnvironmentVariables.AzureFrontDoorId);
            envVarsList.Add(new EnvironmentVarArgs
            {
                Name = ServiceConstants.EnvironmentVariables.AzureFrontDoorId,
                Value = azureFrontDoorId
            });
        }

        if (useKeyVaultSecrets && settings.KeyVault?.Secrets != null)
        {
            logger.LogInformation("Adding {SecretCount} Key Vault secrets as environment variables to Container Apps", settings.KeyVault.Secrets.Count);

            foreach (var secret in settings.KeyVault.Secrets)
            {
                var envVarName = secret.Key
                    .Replace("--", "__", StringComparison.Ordinal)
                    .Replace("-", "_", StringComparison.Ordinal)
                    .ToUpperInvariant();
                var secretRef = secret.Key.ToLowerInvariant();

                if (!envVarNames.Add(envVarName))
                {
                    continue;
                }

                envVarsList.Add(new EnvironmentVarArgs
                {
                    Name = envVarName,
                    SecretRef = secretRef
                });

                logger.LogDebug("Added environment variable {EnvVarName} referencing Container App secret {SecretRef}", envVarName, secretRef);
            }
        }

        if (additionalEnvironmentVariables != null)
        {
            foreach (var envVar in additionalEnvironmentVariables)
            {

                envVarsList.Add(envVar);
            }
        }

        logger.LogInformation("Total environment variables configured for Container App: {Count}", envVarsList.Count);

        return envVarsList.ToArray();
    }
}




