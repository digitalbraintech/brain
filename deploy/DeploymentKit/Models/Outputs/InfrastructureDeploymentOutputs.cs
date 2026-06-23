namespace DeploymentKit.Models.Outputs;

public class InfrastructureDeploymentOutputs
{
    public Output<string> ResourceGroupName { get; set; } = null!;
    public NetworkOutputs Network { get; set; } = null!;
    public ContainerRegistryOutputs ContainerRegistry { get; set; } = null!;
    public DatabaseOutputs Database { get; set; } = null!;
    public CacheOutputs Cache { get; set; } = null!;
    public MonitoringOutputs Monitoring { get; set; } = null!;
    public StorageOutputs Storage { get; set; } = null!;
    public ContainerAppsOutputs ContainerApps { get; set; } = null!;
    public KeyVaultOutputs KeyVault { get; set; } = null!;
    public CertificateOutputs? Certificate { get; set; }
    public ApplicationGatewayOutputs? ApplicationGateway { get; set; }
    public DomainOptimizationOutputs? DomainOptimization { get; set; }
    public FrontDoorOutputs? FrontDoor { get; set; }
    public EventHubsOutputs EventHubs { get; set; } = null!;
    public OpenAiOutputs? OpenAi { get; set; }

    public Output<string> ApiUrl { get; set; } = null!;
    public Output<string> WebsiteUrl { get; set; } = null!;
    public Output<string> MiniAppUrl { get; set; } = null!;
    public Output<string> JobsInternalFqdn { get; set; } = null!;
    public Output<string> AcrLoginServer { get; set; } = null!;
    public Output<string> PostgresHost { get; set; } = null!;
    public Output<string> RedisHost { get; set; } = null!;
}

