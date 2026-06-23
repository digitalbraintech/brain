using DeploymentKit.Deployer;
using DeploymentKit.Enums;
using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using Pulumi;

namespace DigitalBrain.Deploy;

/// <summary>
/// Pulumi program that provisions the Azure infrastructure for DigitalBrain / NeuroOS and brings the
/// gateway live: Azure Container Apps (gateway external, silo internal), Azure Storage (Table clustering +
/// Blob grain/journal), Azure OpenAI (gpt-4o-mini chat deployment), Key Vault, Log Analytics + App Insights,
/// and an Azure Container Registry the apps pull from — all in resource group <c>digitalbrain-rg</c>
/// (westeurope). Run a no-spend plan with <c>pulumi preview</c>; provision for real with <c>pulumi up</c>.
/// </summary>
internal static class Program
{
    private const string SubscriptionId = "08e2e8fa-a9bf-4a1a-be54-56664d2c6cc9";
    private const string Region = "westeurope";
    private const string ResourceGroup = "digitalbrain-rg";
    private const string NamingPrefix = "digitalbrain";

    // Repository names inside the provisioned ACR (Container Apps pull {acrLoginServer}/{repo}:{tag}).
    private const string GatewayRepository = "digitalbrain-gateway";
    private const string SiloRepository = "digitalbrain-silo";
    private const string McpRepository = "digitalbrain-mcp";

    private const string ChatDeploymentName = "chat";

    private static Task<int> Main() => Deployment.RunAsync(ProvisionAsync);

    private static async Task<IDictionary<string, object?>> ProvisionAsync()
    {
        Config config = new();
        string imageTag = config.Get("imageTag")
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_IMAGE_TAG")
            ?? "latest";

        // Phase-1 `up` runs the apps on a public hello-world image so the ACR can be populated before the
        // real images are pulled (phase-2 flips this to false). Defaults to true so a bare `up` is safe.
        bool usePlaceholderImages = config.GetBoolean("usePlaceholderImages") ?? true;

        InfrastructureSettings settings = BuildSettings(imageTag, usePlaceholderImages);
        InfrastructureDeploymentOutputs outputs = await InfrastructureDeployer.DeployAsync(settings);

        return new Dictionary<string, object?>
        {
            ["gatewayFqdn"] = outputs.ApiUrl,
            ["resourceGroup"] = outputs.ResourceGroupName,
            ["acrLoginServer"] = outputs.AcrLoginServer,
            ["openAiEndpoint"] = outputs.OpenAi?.Endpoint ?? Output.Create(string.Empty),
            ["chatDeployment"] = ChatDeploymentName,
            ["imageTag"] = imageTag,
            ["usePlaceholderImages"] = usePlaceholderImages,
            ["environment"] = settings.Environment
        };
    }

    private static InfrastructureSettings BuildSettings(string imageTag, bool usePlaceholderImages)
    {
        OpenAiSettings openAi = new()
        {
            ChatDeploymentName = ChatDeploymentName,
            ChatModelName = "gpt-4o-mini",
            ChatModelVersion = "2024-07-18",
            // gpt-4o-mini in westeurope is only offered as GlobalStandard (no plain "Standard" SKU).
            DeploymentSkuName = "GlobalStandard",
            DeploymentCapacity = 10
        };

        ContainerSettings containers = BuildContainerSettings(imageTag, usePlaceholderImages, openAi.ChatDeploymentName);

        IInfrastructureBuilder builder = InfrastructureDeployer.CreateBuilder()
            .SetName(NamingPrefix)
            .SetEnvironment(EnvironmentType.Production)
            .SetLocation(Region)
            .SetSubscriptionId(SubscriptionId)
            .SetResourceGroupName(ResourceGroup)
            .SetNamingPrefix(NamingPrefix)
            .SetValidationMode(ValidationMode.Basic)
            .SkipAzureAuthValidation()
            // Azure Storage: one StorageV2 account backs Orleans Table clustering + Blob grain/journal.
            // Public network access is required because the Container Apps have no VNet integration.
            .AddStorage(new StorageSettings { AllowPublicNetworkAccess = true })
            .AddTableStorage()
            .AddBlobStorage()
            // Azure OpenAI account + chat model deployment (provisioned by the orchestrator).
            .AddOpenAi(openAi)
            // Log Analytics + Application Insights (required: the Container Apps env logs there).
            .AddInsights()
            // Key Vault holds secrets; managed identity grants the apps read access.
            .AddKeyVault(new KeyVaultSettings { EnableRbacAuthorization = true, ApplyToContainerApps = true })
            // ACA environment + container apps (gateway external on 8080, silo internal).
            .AddContainerApps(containers);

        return builder.Build();
    }

    private static ContainerSettings BuildContainerSettings(string imageTag, bool usePlaceholderImages, string chatDeployment)
    {
        ContainerSettings containers = new()
        {
            UsePlaceholderImages = usePlaceholderImages,
            ApiImageTag = $"{GatewayRepository}:{imageTag}",
            JobsImageTag = $"{SiloRepository}:{imageTag}",
            BotImageTag = $"{McpRepository}:{imageTag}",
            MinReplicas = 1,
            MaxReplicas = 5,
            // Gateway is the only externally reachable app. The real gateway serves 8080; the phase-1
            // placeholder hello-world image listens on 80, so ingress tracks the image being run.
            IngressSettings = new IngressSettings
            {
                External = true,
                TargetPort = usePlaceholderImages ? 80 : 8080,
                Transport = "Http"
            }
        };

        // Plain (non-secret) runtime config; the storage connection string + Azure OpenAI endpoint/key are
        // injected as additional env vars by the Container Apps service from the provisioned resources.
        containers.AdditionalEnvironmentVariables["DIGITALBRAIN_ENV"] = "cloud";
        containers.AdditionalEnvironmentVariables["DigitalBrain__Llm__Provider"] = "azureopenai";
        containers.AdditionalEnvironmentVariables["DigitalBrain__Llm__Model"] = chatDeployment;

        return containers;
    }
}
