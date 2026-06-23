using DeploymentKit.Constants;
using DeploymentKit.Interfaces;
using DeploymentKit.Settings;
using DeploymentKit.Utilities;
using Pulumi.AzureNative.KeyVault;
using Pulumi.AzureNative.KeyVault.Inputs;
using KeyVaultOutputsModel = DeploymentKit.Models.Outputs.KeyVaultOutputs;

namespace DeploymentKit.Services;

public class KeyVaultService(ILogger<KeyVaultService> logger, IResourceNamingService namingService) : IKeyVaultService
{
    public async Task<KeyVaultOutputsModel> CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceGroup);

        if (string.IsNullOrEmpty(settings.NamingPrefix))
            throw new ArgumentException("NamingPrefix cannot be null or empty");

        if (string.IsNullOrEmpty(settings.Environment))
            throw new ArgumentException("Environment cannot be null or empty");

        try
        {
            logger.LogInformation(ServiceConstants.KeyVault.CreationStartMessage, settings.Environment);

            var keyVaultName = namingService.GenerateKeyVaultName(settings.NamingPrefix, settings.Environment);

            // Get tenant ID and client ID from environment variables (for Service Principal auth)
            var tenantId = Environment.GetEnvironmentVariable("ARM_TENANT_ID")
                ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID")
                ?? throw new InvalidOperationException("AZURE_TENANT_ID or ARM_TENANT_ID environment variable is required");

            // RBAC vaults ignore access policies, so the deploying principal's object id is only needed for the
            // legacy access-policy path. Require ARM_CLIENT_ID/AZURE_CLIENT_ID only when RBAC is disabled.
            var rbacEnabled = settings.KeyVault?.EnableRbacAuthorization == true;
            var currentPrincipalId = Environment.GetEnvironmentVariable("ARM_CLIENT_ID")
                ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");

            if (!rbacEnabled && string.IsNullOrEmpty(currentPrincipalId))
            {
                throw new InvalidOperationException("AZURE_CLIENT_ID or ARM_CLIENT_ID environment variable is required");
            }

            // Determine SKU based on environment
            var skuName = settings.Environment.ToLowerInvariant() == ServiceConstants.KeyVault.ProductionEnvironment ? SkuName.Premium : SkuName.Standard;

            var accessPolicies = rbacEnabled
                ? new InputList<AccessPolicyEntryArgs>()
                : GetAccessPolicies(settings, currentPrincipalId!, tenantId);

            var keyVault = new Vault(keyVaultName, new VaultArgs
            {
                ResourceGroupName = resourceGroup,
                VaultName = keyVaultName,
                Location = settings.Location,
                Properties = new VaultPropertiesArgs
                {
                    TenantId = tenantId,
                    Sku = new SkuArgs
                     {
                         Family = "A",
                         Name = skuName
                     },
                    AccessPolicies = accessPolicies,
                    EnabledForDeployment = true,
                    EnabledForTemplateDeployment = true,
                    EnabledForDiskEncryption = true,
                    EnableSoftDelete = settings.KeyVault is { EnableSoftDelete: true },
                    SoftDeleteRetentionInDays = settings.KeyVault.SoftDeleteRetentionDays,
                    EnablePurgeProtection = settings.KeyVault.EnablePurgeProtection ? true : null,
                    EnableRbacAuthorization = settings.KeyVault.EnableRbacAuthorization,
                    NetworkAcls = new NetworkRuleSetArgs
                    {
                        DefaultAction = settings.KeyVault.NetworkAccess.DefaultActionString,
                Bypass = settings.KeyVault.NetworkAccess.BypassString,
                        IpRules = settings.KeyVault.NetworkAccess.AllowedIpRanges.Select(ip => new IPRuleArgs { Value = ip }).ToArray()
                    }
                },
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.KeyVaultType)
            });

            if (settings.KeyVault?.Secrets.Count > 0)
            {
                var createdSecrets = await CreateSecretsAsync(keyVault, keyVaultName, settings, resourceGroup);
                logger.LogInformation("Created {SecretCount} Key Vault secrets in {KeyVaultName}", createdSecrets.Count, keyVaultName);
            }

            logger.LogInformation(ServiceConstants.KeyVault.CreationSuccessMessage, keyVaultName);

            return new KeyVaultOutputsModel
            {
                VaultUri = keyVault.Properties.Apply(p => p.VaultUri!),
                VaultName = keyVault.Name,
                ResourceId = keyVault.Id,
                TenantId = Output.Create(tenantId)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ServiceConstants.KeyVault.CreationFailedMessage, settings.Environment);
            throw;
        }
    }

    private static InputList<AccessPolicyEntryArgs> GetAccessPolicies(InfrastructureSettings settings, string currentPrincipalId, string tenantId)
    {
        var policies = new List<AccessPolicyEntryArgs>
        {
            // Add current user/service principal with full access
            new()
            {
                TenantId = tenantId,
                ObjectId = currentPrincipalId,
                Permissions = new PermissionsArgs
                {
                    Keys = new InputList<Union<string, KeyPermissions>>()
                    {
                        "get", "list", "create", "update", "delete", "backup",
                        "restore", "recover", "purge", "import"
                    },
                    Secrets = new InputList<Union<string, SecretPermissions>>()
                    {
                        "get", "list", "set", "delete", "backup", "restore",
                        "recover", "purge"
                    },
                    Certificates = new InputList<Union<string, CertificatePermissions>>()
                    {
                        "get", "list", "create", "update", "delete", "import",
                        "backup", "restore", "recover", "purge", "managecontacts",
                        "manageissuers", "getissuers", "listissuers", "setissuers", "deleteissuers"
                    }
                }
            }
        };

        // Access policies need to be constructed from the group IDs
        var isDev = !settings.Environment.Equals("prod", StringComparison.InvariantCultureIgnoreCase);

        // Add developer group access policies
        var developerGroupIds = isDev
            ? settings.KeyVault?.EnvironmentAccessPolicies.Development.DeveloperGroupIds
            : []; // No developer access in prod

        if (developerGroupIds != null)
        {
            policies.AddRange(developerGroupIds.Select(groupId => new AccessPolicyEntryArgs
            {
                TenantId = tenantId,
                ObjectId = groupId,
                Permissions = new PermissionsArgs
                {
                    Keys = new InputList<Union<string, KeyPermissions>> { "Get", "List" },
                    Secrets = new InputList<Union<string, SecretPermissions>> { "Get", "List", "Set" },
                    Certificates = new InputList<Union<string, CertificatePermissions>> { "Get", "List" }
                }
            }));
        }

        var adminGroupIds = isDev
            ? [] // No admin groups in dev typically
            : settings.KeyVault?.EnvironmentAccessPolicies.Production.AdminGroupIds;

        if (adminGroupIds != null)
        {
            policies.AddRange(adminGroupIds.Select(groupId => new AccessPolicyEntryArgs
            {
                TenantId = tenantId,
                ObjectId = groupId,
                Permissions = new PermissionsArgs
                {
                    Keys = new InputList<Union<string, KeyPermissions>>
                    {
                        "Get",
                        "List",
                        "Create",
                        "Update",
                        "Delete"
                    },
                    Secrets =
                        new InputList<Union<string, SecretPermissions>> { "Get", "List", "Set", "Delete" },
                    Certificates = new InputList<Union<string, CertificatePermissions>>
                    {
                        "Get",
                        "List",
                        "Create",
                        "Update",
                        "Delete"
                    }
                }
            }));
        }

        return policies.ToArray();
    }

    private static async Task<Dictionary<string, Secret>> CreateSecretsAsync(
        Vault keyVault,
        string keyVaultName,
        InfrastructureSettings settings,
        Input<string> resourceGroup)
    {
        var secrets = new Dictionary<string, Secret>();

        if (settings.KeyVault?.Secrets == null || settings.KeyVault.Secrets.Count == 0)
        {
            return secrets;
        }

        // Get environment-specific secrets configuration
        var isProd = settings.Environment.Equals(ServiceConstants.KeyVault.ProductionEnvironment, StringComparison.InvariantCultureIgnoreCase);

        // Get default content type based on environment
        var defaultContentType = isProd
            ? settings.KeyVault.EnvironmentSecrets.Production.DefaultContentType
            : settings.KeyVault.EnvironmentSecrets.Development.DefaultContentType;

        var defaultExpirationDays = isProd
            ? settings.KeyVault.EnvironmentSecrets.Production.DefaultExpirationDays
            : settings.KeyVault.EnvironmentSecrets.Development.DefaultExpirationDays;
        var expirationDate = DateTime.UtcNow.AddDays(defaultExpirationDays);

        var secretOptions = new CustomResourceOptions
        {
            RetainOnDelete = true,
            DependsOn = { keyVault }
        };

        foreach (var secret in settings.KeyVault.Secrets)
        {
            if (string.IsNullOrWhiteSpace(secret.Key) || string.IsNullOrWhiteSpace(secret.Value))
            {
                continue;
            }

            var secretResource = new Secret($"{keyVaultName}-{secret.Key}", new SecretArgs
            {
                SecretName = secret.Key,
                VaultName = keyVault.Name,
                ResourceGroupName = resourceGroup,
                Properties = new SecretPropertiesArgs
                {
                    Value = Output.CreateSecret(secret.Value),
                    ContentType = defaultContentType,
                    Attributes = new SecretAttributesArgs
                    {
                        Enabled = true,
                        Expires = (int)((DateTimeOffset)expirationDate).ToUnixTimeSeconds()
                    }
                },
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, "secret")
            }, secretOptions);

            secrets[secret.Key] = secretResource;
        }

        return secrets;
    }


    async Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken) => await CreateAsync(settings, resourceGroup, cancellationToken);

    /// <summary>
    /// Explicit implementation of IInfrastructureService.CreateAsync without CancellationToken
    /// </summary>
    async Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup) => await CreateAsync(settings, resourceGroup);
}

