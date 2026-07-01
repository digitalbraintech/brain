using System.Collections.Generic;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Storage = Pulumi.AzureNative.Storage;
using StorageInputs = Pulumi.AzureNative.Storage.Inputs;
using Cognitive = Pulumi.AzureNative.CognitiveServices;
using CognitiveInputs = Pulumi.AzureNative.CognitiveServices.Inputs;
using OpInsights = Pulumi.AzureNative.OperationalInsights;
using OpInsightsInputs = Pulumi.AzureNative.OperationalInsights.Inputs;
using AppInsights = Pulumi.AzureNative.ApplicationInsights;
using App = Pulumi.AzureNative.App;
using AppInputs = Pulumi.AzureNative.App.Inputs;

namespace DigitalBrain.Deploy;

// Minimal Pulumi program for DigitalBrain / NeuroOS. Provisions only what the runtime actually uses:
// a resource group, one StorageV2 account (Orleans Table clustering + Blob grain/journal), Azure OpenAI
// (gpt-4o-mini "chat"), Log Analytics + App Insights, an ACA managed environment, a kernel container app
// with an external Auto-transport ingress, and a Telegram transport container app (external /webhook ingress).
// Replaces the vendored DeploymentKit.
internal static class Program
{
    private const string Region = "westeurope";
    private const string ResourceGroupName = "digitalbrain-rg";
    private const string EnvSuffix = "prod";
    private const string ChatDeploymentName = "chat";

    // Images live in public Docker Hub. ACA pulls without registry creds because the repos are public;
    // otherwise add AppInputs.RegistryCredentialsArgs (server=docker.io) with a Docker Hub access-token secret.
    private const string SiloImageRepository = "docker.io/vhorbachov/digitalbrain-silo";
    private const string TelegramImageRepository = "docker.io/vhorbachov/digitalbrain-telegram";

    // Container App secret names backing the NeuroOS runtime contract.
    private const string StorageConnectionSecret = "digitalbrain-storage-connection";
    private const string OpenAiKeySecret = "digitalbrain-openai-key";
    private const string CheckpointKeySecret = "digitalbrain-checkpoint-key";

    // Telegram transport secrets — bot token + shared internal service key.
    private const string TelegramBotTokenSecret = "telegram-bot-token";
    private const string InternalServiceKeySecret = "digitalbrain-internal-service-key";

    // The env + silo were previously created under DeploymentKit's "app-runtime" component. Alias to that old
    // parent URN so Pulumi re-parents them to the stack root in place instead of replacing the live resources.
    private const string LegacyRuntimeComponentUrn =
        "urn:pulumi:dev::digitalbrain-deploy::DeploymentKit:deploymentkit:DeploymentKitApp::digitalbrain-app-runtime-prod";

    private static Task<int> Main() => Pulumi.Deployment.RunAsync(Provision);

    private static IDictionary<string, object?> Provision()
    {
        var config = new Config();
        var imageTag = config.Get("imageTag")
            ?? System.Environment.GetEnvironmentVariable("DIGITALBRAIN_IMAGE_TAG")
            ?? "latest";

        // CI injects the AES checkpoint-encryption key as a secret env var (from a GitHub Actions secret) so it
        // never lives in git; local runs can instead use `pulumi config set --secret checkpointKey ...`.
        var checkpointKeyEnv = System.Environment.GetEnvironmentVariable("DIGITALBRAIN_CHECKPOINT_KEY");
        var checkpointKey = config.GetSecret("checkpointKey")
            ?? (string.IsNullOrEmpty(checkpointKeyEnv) ? null : Output.CreateSecret(checkpointKeyEnv))
            ?? throw new System.InvalidOperationException(
                "Checkpoint key required: set env DIGITALBRAIN_CHECKPOINT_KEY (CI secret) " +
                "or `pulumi config set --secret digitalbrain-deploy:checkpointKey <base64-32-bytes>` (local).");

        // Telegram transport secrets. GetSecret returns null when not set so a token-less deploy boots idle
        // (transport skips webhook setup when BotToken is empty — same contract as Aspire dev wiring).
        // Set via: pulumi config set --secret telegramBotToken <value>
        //          pulumi config set --secret internalServiceKey <value>
        var telegramBotToken = config.GetSecret("telegramBotToken") ?? Output.CreateSecret(string.Empty);
        var internalServiceKey = config.GetSecret("internalServiceKey") ?? Output.CreateSecret(string.Empty);

        var resourceGroup = new ResourceGroup(ResourceGroupName, new ResourceGroupArgs
        {
            ResourceGroupName = ResourceGroupName,
            Location = Region,
            Tags = StandardTags("resource-group")
        });

        var storage = new Storage.StorageAccount("digitalbrainstprod", new Storage.StorageAccountArgs
        {
            AccountName = "digitalbrainstprod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = Storage.Kind.StorageV2,
            Sku = new StorageInputs.SkuArgs { Name = Storage.SkuName.Standard_LRS },
            AccessTier = Storage.AccessTier.Hot,
            AllowBlobPublicAccess = false,
            AllowSharedKeyAccess = true,
            EnableHttpsTrafficOnly = true,
            MinimumTlsVersion = Storage.MinimumTlsVersion.TLS1_2,
            NetworkRuleSet = new StorageInputs.NetworkRuleSetArgs
            {
                Bypass = Storage.Bypass.AzureServices,
                DefaultAction = Storage.DefaultAction.Allow
            },
            Tags = StandardTags("storage-account")
        });

        var storageKey = Storage.ListStorageAccountKeys.Invoke(new Storage.ListStorageAccountKeysInvokeArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = storage.Name
        }).Apply(keys => keys.Keys[0].Value);

        var storageConnectionString = Output.CreateSecret(Output.Tuple(storage.Name, storageKey).Apply(t =>
            $"DefaultEndpointsProtocol=https;AccountName={t.Item1};AccountKey={t.Item2};EndpointSuffix=core.windows.net"));

        var openAi = new Cognitive.Account("digitalbrainopenaiprod", new Cognitive.AccountArgs
        {
            AccountName = "digitalbrainopenaiprod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = "OpenAI",
            Sku = new CognitiveInputs.SkuArgs { Name = "S0" },
            Identity = new CognitiveInputs.IdentityArgs { Type = Cognitive.ResourceIdentityType.SystemAssigned },
            Properties = new CognitiveInputs.AccountPropertiesArgs
            {
                CustomSubDomainName = "digitalbrainopenaiprod",
                PublicNetworkAccess = Cognitive.PublicNetworkAccess.Enabled
            },
            Tags = StandardTags("azure-openai")
        });

        var chatDeployment = new Cognitive.Deployment(ChatDeploymentName, new Cognitive.DeploymentArgs
        {
            DeploymentName = ChatDeploymentName,
            AccountName = openAi.Name,
            ResourceGroupName = resourceGroup.Name,
            Sku = new CognitiveInputs.SkuArgs { Name = "GlobalStandard", Capacity = 10 },
            Properties = new CognitiveInputs.DeploymentPropertiesArgs
            {
                Model = new CognitiveInputs.DeploymentModelArgs
                {
                    Format = "OpenAI",
                    Name = "gpt-4o-mini",
                    Version = "2024-07-18"
                }
            }
        });

        var openAiEndpoint = openAi.Properties.Apply(p => p.Endpoint ?? string.Empty);
        var openAiKey = Output.CreateSecret(Cognitive.ListAccountKeys.Invoke(new Cognitive.ListAccountKeysInvokeArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = openAi.Name
        }).Apply(keys => keys.Key1 ?? string.Empty));

        var workspace = new OpInsights.Workspace("digitalbrain-log-prod", new OpInsights.WorkspaceArgs
        {
            WorkspaceName = "digitalbrain-log-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Sku = new OpInsightsInputs.WorkspaceSkuArgs { Name = OpInsights.WorkspaceSkuNameEnum.PerGB2018 },
            RetentionInDays = 90,
            Tags = StandardTags("log-analytics")
        });

        _ = new AppInsights.Component("digitalbrain-ai-prod", new AppInsights.ComponentArgs
        {
            ResourceName = "digitalbrain-ai-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = "web",
            ApplicationType = AppInsights.ApplicationType.Web,
            WorkspaceResourceId = workspace.Id,
            Tags = StandardTags("application-insights")
        });

        var workspaceSharedKey = Output.CreateSecret(OpInsights.GetSharedKeys.Invoke(new OpInsights.GetSharedKeysInvokeArgs
        {
            ResourceGroupName = resourceGroup.Name,
            WorkspaceName = workspace.Name
        }).Apply(k => k.PrimarySharedKey ?? string.Empty));

        var containerEnvironment = new App.ManagedEnvironment("digitalbrain-cae-prod", new App.ManagedEnvironmentArgs
        {
            EnvironmentName = "digitalbrain-cae-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            AppLogsConfiguration = new AppInputs.AppLogsConfigurationArgs
            {
                Destination = "log-analytics",
                LogAnalyticsConfiguration = new AppInputs.LogAnalyticsConfigurationArgs
                {
                    CustomerId = workspace.CustomerId,
                    SharedKey = workspaceSharedKey
                }
            },
            Tags = StandardTags("container-apps-environment")
        }, AliasOldRuntimeParent());

        var siloImage = Output.Format($"{SiloImageRepository}:{imageTag}");
        var telegramImage = Output.Format($"{TelegramImageRepository}:{imageTag}");

        var silo = new App.ContainerApp("digitalbrain-jobs", new App.ContainerAppArgs
        {
            ContainerAppName = "digitalbrain-jobs",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            ManagedEnvironmentId = containerEnvironment.Id,
            Configuration = new AppInputs.ConfigurationArgs
            {
                Ingress = new AppInputs.IngressArgs
                {
                    External = true,
                    TargetPort = 8080,
                    Transport = "Auto"
                },
                Secrets =
                {
                    new AppInputs.SecretArgs { Name = StorageConnectionSecret, Value = storageConnectionString },
                    new AppInputs.SecretArgs { Name = OpenAiKeySecret, Value = openAiKey },
                    new AppInputs.SecretArgs { Name = CheckpointKeySecret, Value = checkpointKey }
                }
            },
            Template = new AppInputs.TemplateArgs
            {
                Containers =
                {
                    new AppInputs.ContainerArgs
                    {
                        Name = "jobs",
                        Image = siloImage,
                        Resources = new AppInputs.ContainerResourcesArgs { Cpu = 1.0, Memory = "2Gi" },
                        Env =
                        {
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_WEB_PORT", Value = "8080" },
                            new AppInputs.EnvironmentVarArgs { Name = "DIGITALBRAIN_ENV", Value = "cloud" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__Provider", Value = "azureopenai" },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__Model", Value = ChatDeploymentName },
                            new AppInputs.EnvironmentVarArgs { Name = "ConnectionStrings__clustering", SecretRef = StorageConnectionSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "ConnectionStrings__grainstate", SecretRef = StorageConnectionSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "ConnectionStrings__journal", SecretRef = StorageConnectionSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIEndpoint", Value = openAiEndpoint },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Llm__AzureOpenAIKey", SecretRef = OpenAiKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__Checkpoint__Key", SecretRef = CheckpointKeySecret }
                        }
                    }
                },
                Scale = new AppInputs.ScaleArgs { MinReplicas = 1, MaxReplicas = 5 }
            },
            Tags = StandardTags("container-app-jobs")
        }, AliasOldRuntimeParent());

        // The Telegram transport calls the kernel's gRPC gateway. The silo's external FQDN is reachable from
        // within the same ACA environment, so we build the address from LatestRevisionFqdn. Must be HTTPS
        // because ACA external ingress always terminates TLS. Same key the Aspire dev wiring sets:
        // "DigitalBrain:GatewayAddress" (colon separator, mirrored in transport Program.cs).
        var kernelGatewayAddress = silo.LatestRevisionFqdn.Apply(fqdn => $"https://{fqdn}");

        var telegramTransport = new App.ContainerApp("digitalbrain-telegram", new App.ContainerAppArgs
        {
            ContainerAppName = "digitalbrain-telegram",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            ManagedEnvironmentId = containerEnvironment.Id,
            Configuration = new AppInputs.ConfigurationArgs
            {
                // External ingress so Telegram's servers can POST to /webhook.
                Ingress = new AppInputs.IngressArgs
                {
                    External = true,
                    TargetPort = 8080,
                    Transport = "Http"
                },
                Secrets =
                {
                    new AppInputs.SecretArgs { Name = TelegramBotTokenSecret, Value = telegramBotToken },
                    new AppInputs.SecretArgs { Name = InternalServiceKeySecret, Value = internalServiceKey }
                }
            },
            Template = new AppInputs.TemplateArgs
            {
                Containers =
                {
                    new AppInputs.ContainerArgs
                    {
                        Name = "telegram",
                        Image = telegramImage,
                        Resources = new AppInputs.ContainerResourcesArgs { Cpu = 0.25, Memory = "0.5Gi" },
                        Env =
                        {
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                            new AppInputs.EnvironmentVarArgs { Name = "ASPNETCORE_HTTP_PORTS", Value = "8080" },
                            // Same keys read by transport Program.cs and set by WireTelegramTransport for dev parity.
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__GatewayAddress", Value = kernelGatewayAddress },
                            new AppInputs.EnvironmentVarArgs { Name = "Telegram__BotToken", SecretRef = TelegramBotTokenSecret },
                            new AppInputs.EnvironmentVarArgs { Name = "DigitalBrain__InternalServiceKey", SecretRef = InternalServiceKeySecret },
                            new AppInputs.EnvironmentVarArgs { Name = "Telegram__PackName", Value = "DigitalBrain.Telegram.Responder" },
                            new AppInputs.EnvironmentVarArgs { Name = "Telegram__ConfigScope", Value = "default" }
                        }
                    }
                },
                Scale = new AppInputs.ScaleArgs { MinReplicas = 1, MaxReplicas = 3 }
            },
            Tags = StandardTags("container-app-telegram")
        });

        return new Dictionary<string, object?>
        {
            ["resourceGroup"] = resourceGroup.Name,
            ["storageAccount"] = storage.Name,
            ["openAiEndpoint"] = openAiEndpoint,
            ["chatDeployment"] = ChatDeploymentName,
            ["siloApp"] = silo.Name,
            ["telegramApp"] = telegramTransport.Name,
            ["telegramFqdn"] = telegramTransport.LatestRevisionFqdn,
            ["imageTag"] = imageTag,
            ["environment"] = EnvSuffix
        };
    }

    private static CustomResourceOptions AliasOldRuntimeParent() =>
        new() { Aliases = { new Alias { ParentUrn = LegacyRuntimeComponentUrn } } };

    private static InputMap<string> StandardTags(string resourceType) => new Dictionary<string, string>
    {
        ["Environment"] = EnvSuffix,
        ["Project"] = "Application",
        ["Owner"] = "Application-DevOps",
        ["CorrelationId"] = "unknown",
        ["CreatedBy"] = "Pulumi",
        ["ManagedBy"] = "Pulumi",
        ["ResourceType"] = resourceType
    };
}
