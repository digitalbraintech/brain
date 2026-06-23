using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using DeploymentKit.Utilities;
using Pulumi.AzureNative.CognitiveServices;
using Pulumi.AzureNative.CognitiveServices.Inputs;
using CognitiveDeployment = Pulumi.AzureNative.CognitiveServices.Deployment;

namespace DeploymentKit.Services;

/// <summary>
/// Provisions an Azure OpenAI account (Cognitive Services, kind "OpenAI") together with a chat model deployment.
/// </summary>
public class OpenAiService(ILogger<OpenAiService> logger, IResourceNamingService namingService) : IOpenAiService
{
    private readonly ILogger<OpenAiService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IResourceNamingService _namingService = namingService ?? throw new ArgumentNullException(nameof(namingService));

    public Task<OpenAiOutputs> CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceGroup);

        OpenAiSettings? openAi = settings.OpenAi;
        if (openAi is null || !openAi.Enabled)
        {
            _logger.LogInformation("Azure OpenAI settings not provided. Skipping Azure OpenAI provisioning.");
            return Task.FromResult(new OpenAiOutputs
            {
                Name = string.Empty,
                AccountName = Output.Create(string.Empty),
                Endpoint = Output.Create(string.Empty),
                PrimaryKey = Output.CreateSecret(string.Empty),
                ChatDeploymentName = Output.Create(string.Empty),
                ResourceId = Output.Create(string.Empty)
            });
        }

        cancellationToken.ThrowIfCancellationRequested();

        string accountName = _namingService.GenerateOpenAiAccountName(settings.NamingPrefix, settings.Environment);
        _logger.LogInformation("Creating Azure OpenAI account {AccountName} in {Location}.", accountName, settings.Location);

        var account = new Account(accountName, new AccountArgs
        {
            AccountName = accountName,
            ResourceGroupName = resourceGroup,
            Location = settings.Location,
            Kind = "OpenAI",
            Sku = new SkuArgs { Name = openAi.SkuName },
            Identity = new IdentityArgs { Type = ResourceIdentityType.SystemAssigned },
            Properties = new AccountPropertiesArgs
            {
                CustomSubDomainName = accountName,
                PublicNetworkAccess = openAi.EnablePublicNetworkAccess
                    ? Pulumi.AzureNative.CognitiveServices.PublicNetworkAccess.Enabled
                    : Pulumi.AzureNative.CognitiveServices.PublicNetworkAccess.Disabled
            },
            Tags = ResourceTagHelper.GetStandardTags(settings.Environment, "azure-openai")
        });

        var chatDeployment = new CognitiveDeployment(openAi.ChatDeploymentName, new DeploymentArgs
        {
            AccountName = account.Name,
            ResourceGroupName = resourceGroup,
            DeploymentName = openAi.ChatDeploymentName,
            Sku = new SkuArgs
            {
                Name = openAi.DeploymentSkuName,
                Capacity = openAi.DeploymentCapacity
            },
            Properties = new DeploymentPropertiesArgs
            {
                Model = new DeploymentModelArgs
                {
                    Format = "OpenAI",
                    Name = openAi.ChatModelName,
                    Version = openAi.ChatModelVersion
                }
            }
        });

        var accountKeys = ListAccountKeys.Invoke(new ListAccountKeysInvokeArgs
        {
            ResourceGroupName = resourceGroup,
            AccountName = account.Name
        });

        var outputs = new OpenAiOutputs
        {
            Name = accountName,
            AccountName = account.Name,
            Endpoint = account.Properties.Apply(p => p.Endpoint ?? string.Empty),
            PrimaryKey = Output.CreateSecret(accountKeys.Apply(k => k.Key1 ?? string.Empty)),
            ChatDeploymentName = chatDeployment.Name,
            ResourceId = account.Id
        };

        _logger.LogInformation("Azure OpenAI account {AccountName} configured with chat deployment {Deployment}.", accountName, openAi.ChatDeploymentName);
        return Task.FromResult(outputs);
    }

    async Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken) => await CreateAsync(settings, resourceGroup, cancellationToken);

    async Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup) => await CreateAsync(settings, resourceGroup);
}
