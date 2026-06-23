using DeploymentKit.Components;
using DeploymentKit.Constants;
using DeploymentKit.Exceptions;
using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Settings;
using DeploymentKit.Utilities;
using Pulumi.AzureNative.Network;
using Pulumi.AzureNative.Network.Inputs;
using System.Diagnostics;
using NetworkSecurityGroupArgs = Pulumi.AzureNative.Network.NetworkSecurityGroupArgs;
using NetworkSecurityGroupInputArgs = Pulumi.AzureNative.Network.Inputs.NetworkSecurityGroupArgs;
using SecurityRuleArgs = Pulumi.AzureNative.Network.Inputs.SecurityRuleArgs;
using SubnetArgs = Pulumi.AzureNative.Network.SubnetArgs;
using PrivateDnsZone = Pulumi.AzureNative.PrivateDns.PrivateZone;
using PrivateDnsZoneArgs = Pulumi.AzureNative.PrivateDns.PrivateZoneArgs;
using VirtualNetworkLink = Pulumi.AzureNative.PrivateDns.VirtualNetworkLink;
using VirtualNetworkLinkArgs = Pulumi.AzureNative.PrivateDns.VirtualNetworkLinkArgs;
using PrivateDnsSubResourceArgs = Pulumi.AzureNative.PrivateDns.Inputs.SubResourceArgs;

namespace DeploymentKit.Services;

/// <summary>
/// Service for managing network infrastructure including VNets, subnets, and private endpoints
/// </summary>
public class NetworkService(ILogger<NetworkService> logger, IResourceNamingService namingService, ICorrelationIdService correlationIdService) : INetworkService, IInfrastructureService
{
    private readonly ILogger<NetworkService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IResourceNamingService _namingService = namingService ?? throw new ArgumentNullException(nameof(namingService));
    private readonly ICorrelationIdService _correlationIdService = correlationIdService ?? throw new ArgumentNullException(nameof(correlationIdService));
    public Task<NetworkOutputs> CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceGroup);

        var componentName = $"{settings.NamingPrefix}-network-{settings.Environment}";
        return DeploymentKitNetwork.CreateAsync(componentName, () => CreateCoreAsync(settings, resourceGroup, cancellationToken));
    }

    private async Task<NetworkOutputs> CreateCoreAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resourceGroup);

        if (string.IsNullOrEmpty(settings.NamingPrefix))
            throw new ArgumentException("NamingPrefix cannot be null or empty");

        if (string.IsNullOrEmpty(settings.Environment))
            throw new ArgumentException("Environment cannot be null or empty");

        // Use correlation ID from service instead of generating new one
        var correlationId = _correlationIdService.GetOrGenerateCorrelationId();
        var stopwatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [LoggingConstants.PropertyNames.CorrelationId] = correlationId,
            [LoggingConstants.PropertyNames.Environment] = settings.Environment,
            [LoggingConstants.PropertyNames.Service] = LoggingConstants.ServiceNames.NetworkService,
            [LoggingConstants.PropertyNames.Operation] = ServiceConstants.ServiceOperations.CreateNetworkInfrastructure
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip VNet creation for public environments without subnets configured (or when networking is unused)
            if (settings.Network == null ||
                string.IsNullOrEmpty(settings.Network.VNetAddressSpace) ||
                string.IsNullOrEmpty(settings.Network.ContainerAppsSubnet))
            {
                _logger.LogInformation("Skipping VNet creation for public environment (IsInternalEnvironment=false) for CorrelationId: {CorrelationId}", correlationId);

                stopwatch.Stop();
                _logger.LogInformation("Network service completed in {ElapsedMs}ms (public mode - no VNet) for CorrelationId: {CorrelationId}",
                    stopwatch.ElapsedMilliseconds, correlationId);

                // Return empty network outputs for public Container Apps environment
                return new NetworkOutputs
                {
                    VirtualNetworkId = Output.Create(string.Empty),
                    VirtualNetworkName = Output.Create(string.Empty),
                    ContainerAppsSubnetId = Output.Create(string.Empty),
                    DatabaseSubnetId = Output.Create(string.Empty),
                    PrivateEndpointsSubnetId = Output.Create(string.Empty),
                    ApplicationGatewaySubnetId = Output.Create(string.Empty),
                    ContainerAppsNsgId = Output.Create(string.Empty),
                    DatabaseNsgId = Output.Create(string.Empty),
                    ContainerAppsPrivateDnsZoneId = Output.Create(string.Empty),
                    DatabasePrivateDnsZoneId = Output.Create(string.Empty)
                };
            }

            _logger.LogInformation(ServiceConstants.Network.CreationStartMessage,
                settings.Environment, correlationId, settings.Network.VNetAddressSpace);

            // Create Virtual Network
            var vnetStopwatch = Stopwatch.StartNew();
            var vnet = await CreateVirtualNetworkAsync(settings, resourceGroup, correlationId);
            _logger.LogInformation("Virtual Network created in {ElapsedMs}ms for CorrelationId: {CorrelationId}", vnetStopwatch.ElapsedMilliseconds, correlationId);

            // Create Network Security Groups in parallel
            var nsgStopwatch = Stopwatch.StartNew();
            _logger.LogDebug("Creating Network Security Groups in parallel for CorrelationId: {CorrelationId}", correlationId);

            var containerAppsNsgTask = CreateContainerAppsNsgAsync(settings, resourceGroup, correlationId);
            var databaseNsgTask = CreateDatabaseNsgAsync(settings, resourceGroup, correlationId);

            await Task.WhenAll(containerAppsNsgTask, databaseNsgTask);
            var containerAppsNsg = await containerAppsNsgTask;
            var databaseNsg = await databaseNsgTask;

            _logger.LogInformation("Network Security Groups created in {ElapsedMs}ms for CorrelationId: {CorrelationId}",  nsgStopwatch.ElapsedMilliseconds, correlationId);

            // Create Subnets in parallel
            var subnetStopwatch = Stopwatch.StartNew();
            _logger.LogDebug("Creating subnets in parallel for CorrelationId: {CorrelationId}", correlationId);

            var containerAppsSubnetTask = CreateContainerAppsSubnetAsync(settings, resourceGroup, vnet, containerAppsNsg, correlationId);
            var databaseSubnetTask = CreateDatabaseSubnetAsync(settings, resourceGroup, vnet, databaseNsg, correlationId);
            var privateEndpointsSubnetTask = CreatePrivateEndpointsSubnetAsync(settings, resourceGroup, vnet, correlationId);
            var appGatewaySubnetTask = CreateApplicationGatewaySubnetAsync(settings, resourceGroup, vnet, correlationId);

            await Task.WhenAll(containerAppsSubnetTask, databaseSubnetTask, privateEndpointsSubnetTask, appGatewaySubnetTask);
            var containerAppsSubnet = await containerAppsSubnetTask;
            var databaseSubnet = await databaseSubnetTask;
            var privateEndpointsSubnet = await privateEndpointsSubnetTask;
            var appGatewaySubnet = await appGatewaySubnetTask;

            _logger.LogInformation("All subnets created in {ElapsedMs}ms for CorrelationId: {CorrelationId}. Subnets: ContainerApps, Database, PrivateEndpoints, ApplicationGateway", subnetStopwatch.ElapsedMilliseconds, correlationId);

            // Create Private DNS Zones in parallel
            var dnsStopwatch = Stopwatch.StartNew();
            _logger.LogDebug(ServiceConstants.Network.DnsZoneCreationParallelMessage, correlationId);

            var containerAppsPrivateDnsZoneTask = CreateContainerAppsPrivateDnsZoneAsync(settings, resourceGroup, vnet, correlationId);
            var databasePrivateDnsZoneTask = CreateDatabasePrivateDnsZoneAsync(settings, resourceGroup, vnet, correlationId);

            await Task.WhenAll(containerAppsPrivateDnsZoneTask, databasePrivateDnsZoneTask);
            var containerAppsPrivateDnsZone = await containerAppsPrivateDnsZoneTask;
            var databasePrivateDnsZone = await databasePrivateDnsZoneTask;

            _logger.LogInformation(ServiceConstants.Network.DnsZonesCreatedMessage, dnsStopwatch.ElapsedMilliseconds, correlationId);

            var outputs = new NetworkOutputs
            {
                VirtualNetworkId = vnet.Id,
                VirtualNetworkName = vnet.Name,
                ContainerAppsSubnetId = containerAppsSubnet.Id,
                DatabaseSubnetId = databaseSubnet.Id,
                PrivateEndpointsSubnetId = privateEndpointsSubnet.Id,
                ApplicationGatewaySubnetId = appGatewaySubnet.Id,
                ContainerAppsNsgId = containerAppsNsg.Id,
                DatabaseNsgId = databaseNsg.Id,
                ContainerAppsPrivateDnsZoneId = containerAppsPrivateDnsZone.Id,
                DatabasePrivateDnsZoneId = databasePrivateDnsZone.Id
            };

            stopwatch.Stop();

            _logger.LogInformation(ServiceConstants.Network.CreationSuccessMessage, settings.Environment, stopwatch.ElapsedMilliseconds, correlationId, "virtual-network");

            return outputs;
        }
        catch (ResourceCreationException)
        {
            // Re-throw ResourceCreationException from inner methods without wrapping
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(ServiceConstants.Network.CreationCancelledMessage, settings.Environment, stopwatch.ElapsedMilliseconds, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, ServiceConstants.Network.CreationFailedMessage, settings.Environment, stopwatch.ElapsedMilliseconds, correlationId, ex.Message);

            throw new ResourceCreationException(
                $"Failed to create network infrastructure for environment '{settings.Environment}' (CorrelationId: {correlationId})",
                ex,
                ServiceConstants.ResourceTypes.Network,
                ServiceConstants.ResourceTypes.VirtualNetworkSubnets,
                settings.Environment,
                correlationId,
                ServiceConstants.ErrorCodes.NetworkCreationFailed);
        }
    }

    private Task<VirtualNetwork> CreateVirtualNetworkAsync(InfrastructureSettings settings, Input<string> resourceGroup, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [LoggingConstants.PropertyNames.ResourceType] = ServiceConstants.ResourceTypes.VirtualNetwork,
            [LoggingConstants.PropertyNames.CorrelationId] = correlationId
        });

        try
        {
            var vnetName = _namingService.GenerateVirtualNetworkName(settings.NamingPrefix, settings.Environment);

            _logger.LogInformation("Creating Virtual Network: {VNetName} with address space: {AddressSpace} for CorrelationId: {CorrelationId}",
                vnetName, settings.Network?.VNetAddressSpace, correlationId);

            var vnet = new VirtualNetwork(vnetName, new global::Pulumi.AzureNative.Network.VirtualNetworkArgs
            {
                VirtualNetworkName = vnetName,
                ResourceGroupName = resourceGroup,
                Location = settings.Location,
                AddressSpace = new AddressSpaceArgs
                {
                    AddressPrefixes = new[] { settings.Network?.VNetAddressSpace }
                },
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, "virtual-network", correlationId)
            }, ComponentResourceScope.CreateChildOptions(vnetName));

            _logger.LogDebug("Virtual Network {VNetName} configured successfully for CorrelationId: {CorrelationId}", vnetName, correlationId);
            return Task.FromResult(vnet);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi context exception in CreateVirtualNetworkAsync: {Message}", ex.Message);
            throw new ResourceCreationException(
                $"Pulumi context error creating Virtual Network",
                ex,
                ServiceConstants.ResourceTypes.VirtualNetwork,
                ServiceConstants.ResourceTypes.Network,
                correlationId,
                correlationId,
                ServiceConstants.ErrorCodes.NetworkCreationFailed);
        }
        catch (Exception ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi context exception in CreateVirtualNetworkAsync: {Message}", ex.Message);
            throw new ResourceCreationException(
                $"Pulumi context error creating Virtual Network",
                ex,
                ServiceConstants.ResourceTypes.VirtualNetwork,
                ServiceConstants.ResourceTypes.Network,
                correlationId,
                correlationId,
                ServiceConstants.ErrorCodes.NetworkCreationFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Virtual Network for CorrelationId: {CorrelationId}. Error: {ErrorMessage}", correlationId, ex.Message);
            throw;
        }
    }

    private Task<NetworkSecurityGroup> CreateContainerAppsNsgAsync(InfrastructureSettings settings, Input<string> resourceGroup, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "NetworkSecurityGroup",
            ["NSGType"] = "ContainerApps",
            ["CorrelationId"] = correlationId
        });

        try
        {
            var nsgName = _namingService.GenerateNetworkSecurityGroupName(settings.NamingPrefix, "containerapp", settings.Environment);

            _logger.LogInformation("Creating Container Apps NSG: {NsgName} for CorrelationId: {CorrelationId}", nsgName, correlationId);

            var nsg = new NetworkSecurityGroup(nsgName, new NetworkSecurityGroupArgs
            {
                NetworkSecurityGroupName = nsgName,
                ResourceGroupName = resourceGroup,
                Location = settings.Location,
                SecurityRules = new[]
                {
                    // Allow HTTPS inbound from Application Gateway
                    new SecurityRuleArgs
                    {
                        Name = DeploymentConstants.Network.AllowHttpsFromAppGatewayRule,
                        Priority = DeploymentConstants.Network.HttpsFromAppGatewayPriority,
                        Direction = DeploymentConstants.Network.InboundDirection,
                        Access = DeploymentConstants.Network.AllowAccess,
                        Protocol = DeploymentConstants.Network.TcpProtocol,
                        SourcePortRange = DeploymentConstants.Network.AllAddresses,
                        DestinationPortRange = DeploymentConstants.Network.HttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SourceAddressPrefix = settings.Network.ApplicationGatewaySubnet,
                        DestinationAddressPrefix = settings.Network.ContainerAppsSubnet
                    },
                    // Allow HTTP inbound from Application Gateway
                    new SecurityRuleArgs
                    {
                        Name = DeploymentConstants.Network.AllowHttpFromAppGatewayRule,
                        Priority = DeploymentConstants.Network.HttpFromAppGatewayPriority,
                        Direction = DeploymentConstants.Network.InboundDirection,
                        Access = DeploymentConstants.Network.AllowAccess,
                        Protocol = DeploymentConstants.Network.TcpProtocol,
                        SourcePortRange = DeploymentConstants.Network.AllAddresses,
                        DestinationPortRange = DeploymentConstants.Network.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SourceAddressPrefix = settings.Network.ApplicationGatewaySubnet,
                        DestinationAddressPrefix = settings.Network.ContainerAppsSubnet
                    },
                    // Allow Container Apps internal communication
                    new SecurityRuleArgs
                    {
                        Name = DeploymentConstants.Network.AllowContainerAppsInternalRule,
                        Priority = DeploymentConstants.Network.ContainerAppsInternalPriority,
                        Direction = DeploymentConstants.Network.InboundDirection,
                        Access = DeploymentConstants.Network.AllowAccess,
                        Protocol = DeploymentConstants.Network.AllProtocols,
                        SourcePortRange = DeploymentConstants.Network.AllAddresses,
                        DestinationPortRange = DeploymentConstants.Network.AllAddresses,
                        SourceAddressPrefix = settings.Network.ContainerAppsSubnet,
                        DestinationAddressPrefix = settings.Network.ContainerAppsSubnet
                    },
                    // Deny all other inbound traffic
                    new SecurityRuleArgs
                    {
                        Name = DeploymentConstants.Network.DenyAllInboundRule,
                        Priority = DeploymentConstants.Network.DenyAllInboundPriority,
                        Direction = DeploymentConstants.Network.InboundDirection,
                        Access = DeploymentConstants.Network.DenyAccess,
                        Protocol = DeploymentConstants.Network.AllProtocols,
                        SourcePortRange = DeploymentConstants.Network.AllAddresses,
                        DestinationPortRange = DeploymentConstants.Network.AllAddresses,
                        SourceAddressPrefix = DeploymentConstants.Network.AllAddresses,
                        DestinationAddressPrefix = DeploymentConstants.Network.AllAddresses
                    }
                },
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.NetworkSecurityGroupType, correlationId)
            }, ComponentResourceScope.CreateChildOptions(nsgName));

            return Task.FromResult(nsg);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi context exception in CreateContainerAppsNsgAsync: {Message}", ex.Message);
            throw new ResourceCreationException(
                "Pulumi context error creating Container Apps NSG",
                ex,
                ServiceConstants.ResourceTypes.NetworkSecurityGroup,
                ServiceConstants.ResourceTypes.Network,
                correlationId,
                correlationId,
                ServiceConstants.ErrorCodes.NetworkCreationFailed);
        }
        catch (Exception ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi context exception in CreateContainerAppsNsgAsync: {Message}", ex.Message);
            throw new ResourceCreationException(
                "Pulumi context error creating Container Apps NSG",
                ex,
                ServiceConstants.ResourceTypes.NetworkSecurityGroup,
                ServiceConstants.ResourceTypes.Network,
                correlationId,
                correlationId,
                ServiceConstants.ErrorCodes.NetworkCreationFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ServiceConstants.Network.NsgCreationFailedMessage, correlationId, ex.Message);
            throw;
        }
    }

    private Task<NetworkSecurityGroup> CreateDatabaseNsgAsync(InfrastructureSettings settings, Input<string> resourceGroup, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "NetworkSecurityGroup",
            ["NSGType"] = DeploymentConstants.ResourceTags.DatabaseType,
            ["CorrelationId"] = correlationId
        });

        try
        {
            var nsgName = _namingService.GenerateNetworkSecurityGroupName(settings.NamingPrefix, DeploymentConstants.ResourceTags.DatabaseType, settings.Environment);

            _logger.LogInformation(ServiceConstants.Network.DatabaseNsgCreationMessage, nsgName, correlationId);

            var nsg = new NetworkSecurityGroup(nsgName, new NetworkSecurityGroupArgs
            {
                NetworkSecurityGroupName = nsgName,
                ResourceGroupName = resourceGroup,
                Location = settings.Location,
                SecurityRules = new[]
                {
                    // Allow PostgreSQL from Container Apps
                    new SecurityRuleArgs
                    {
                        Name = DeploymentConstants.Network.AllowPostgreSQLFromContainerAppsRule,
                        Priority = DeploymentConstants.Network.PostgreSqlFromContainerAppsPriority,
                        Direction = DeploymentConstants.Network.InboundDirection,
                        Access = DeploymentConstants.Network.AllowAccess,
                        Protocol = DeploymentConstants.Network.TcpProtocol,
                        SourcePortRange = DeploymentConstants.Network.AllAddresses,
                        DestinationPortRange = DeploymentConstants.Network.PostgreSqlPort,
                        SourceAddressPrefix = settings.Network.ContainerAppsSubnet,
                        DestinationAddressPrefix = settings.Network.DatabaseSubnet
                    },
                    // Deny all other inbound traffic
                    new SecurityRuleArgs
                    {
                        Name = DeploymentConstants.Network.DenyAllInboundRule,
                        Priority = DeploymentConstants.Network.DenyAllInboundPriority,
                        Direction = DeploymentConstants.Network.InboundDirection,
                        Access = DeploymentConstants.Network.DenyAccess,
                        Protocol = DeploymentConstants.Network.AllProtocols,
                        SourcePortRange = DeploymentConstants.Network.AllAddresses,
                        DestinationPortRange = DeploymentConstants.Network.AllAddresses,
                        SourceAddressPrefix = DeploymentConstants.Network.AllAddresses,
                        DestinationAddressPrefix = DeploymentConstants.Network.AllAddresses
                    }
                },
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.NetworkSecurityGroupType, correlationId)
            }, ComponentResourceScope.CreateChildOptions(nsgName));

            _logger.LogDebug(ServiceConstants.Network.DatabaseNsgConfiguredMessage, nsgName, correlationId);
            return Task.FromResult(nsg);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi context exception in CreateDatabaseNsgAsync: {Message}", ex.Message);
            throw new ResourceCreationException($"Pulumi context error in database NSG creation: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi-related exception in CreateDatabaseNsgAsync: {Message}", ex.Message);
            throw new ResourceCreationException($"Pulumi-related error in database NSG creation: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ServiceConstants.Network.DatabaseNsgCreationFailedMessage, correlationId, ex.Message);
            throw;
        }
    }

    private Task<Subnet> CreateContainerAppsSubnetAsync(InfrastructureSettings settings, Input<string> resourceGroup, VirtualNetwork vnet, NetworkSecurityGroup nsg, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "Subnet",
            ["SubnetType"] = "ContainerApps",
            ["CorrelationId"] = correlationId
        });

        try
        {
            var subnetName = _namingService.GenerateSubnetName(DeploymentConstants.Network.ContainerAppSubnetType, settings.Environment);

            _logger.LogInformation("Creating Container Apps Subnet: {SubnetName} with address prefix: {AddressPrefix} for CorrelationId: {CorrelationId}",
                subnetName, settings.Network.ContainerAppsSubnet, correlationId);

            var subnet = new Subnet(subnetName, new SubnetArgs
            {
                SubnetName = subnetName,
                ResourceGroupName = resourceGroup,
                VirtualNetworkName = vnet.Name,
                AddressPrefix = settings.Network.ContainerAppsSubnet,
                NetworkSecurityGroup = new NetworkSecurityGroupInputArgs
                {
                    Id = nsg.Id
                },
                Delegations = new[]
                {
                    new DelegationArgs
                    {
                        Name = DeploymentConstants.Network.ContainerAppEnvironmentDelegation,
                        ServiceName = DeploymentConstants.Network.ContainerAppEnvironmentService
                    }
                }
            }, ComponentResourceScope.CreateChildOptions(subnetName));

            _logger.LogDebug("Container Apps Subnet {SubnetName} configured with delegation to Microsoft.App/environments for CorrelationId: {CorrelationId}", subnetName, correlationId);
            return Task.FromResult(subnet);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi context exception in CreateContainerAppsSubnetAsync: {Message}", ex.Message);
            throw new ResourceCreationException($"Pulumi context error in Container Apps subnet creation: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi-related exception in CreateContainerAppsSubnetAsync: {Message}", ex.Message);
            throw new ResourceCreationException($"Pulumi-related error in Container Apps subnet creation: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Container Apps Subnet for CorrelationId: {CorrelationId}. Error: {ErrorMessage}", correlationId, ex.Message);
            throw;
        }
    }

    private Task<Subnet> CreateDatabaseSubnetAsync(InfrastructureSettings settings, Input<string> resourceGroup, VirtualNetwork vnet, NetworkSecurityGroup nsg, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "Subnet",
            ["SubnetType"] = DeploymentConstants.ResourceTags.DatabaseType,
            ["CorrelationId"] = correlationId
        });

        try
        {
            var subnetName = _namingService.GenerateSubnetName(DeploymentConstants.Network.DatabaseSubnetType, settings.Environment);

            _logger.LogInformation("Creating Database Subnet: {SubnetName} with address prefix: {AddressPrefix} for CorrelationId: {CorrelationId}", subnetName, settings.Network.DatabaseSubnet, correlationId);

            var subnet = new Subnet(subnetName, new SubnetArgs
            {
                SubnetName = subnetName,
                ResourceGroupName = resourceGroup,
                VirtualNetworkName = vnet.Name,
                AddressPrefix = settings.Network.DatabaseSubnet,
                NetworkSecurityGroup = new NetworkSecurityGroupInputArgs
                {
                    Id = nsg.Id
                },
                Delegations = new[]
                {
                    new DelegationArgs
                    {
                        Name = DeploymentConstants.Network.PostgreSQLFlexibleServerDelegation,
                        ServiceName = DeploymentConstants.Network.PostgreSQLFlexibleServerService
                    }
                }
            }, ComponentResourceScope.CreateChildOptions(subnetName));

            _logger.LogDebug(ServiceConstants.Network.DatabaseSubnetConfiguredMessage, subnetName, correlationId);
            return Task.FromResult(subnet);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi context exception in CreateDatabaseSubnetAsync: {Message}", ex.Message);
            throw new ResourceCreationException($"Pulumi context error in Database subnet creation: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex.Message.Contains("Pulumi") || ex.Message.Contains("Deployment.Instance"))
        {
            _logger.LogWarning("Pulumi-related exception in CreateDatabaseSubnetAsync: {Message}", ex.Message);
            throw new ResourceCreationException($"Pulumi-related error in Database subnet creation: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ServiceConstants.Network.DatabaseSubnetCreationFailedMessage, correlationId, ex.Message);
            throw;
        }
    }

    private Task<Subnet> CreatePrivateEndpointsSubnetAsync(InfrastructureSettings settings, Input<string> resourceGroup, VirtualNetwork vnet, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "Subnet",
            ["SubnetType"] = "PrivateEndpoints",
            ["CorrelationId"] = correlationId
        });

        try
        {
            var subnetName = _namingService.GenerateSubnetName(DeploymentConstants.Network.PrivateEndpointsSubnetType, settings.Environment);

            _logger.LogInformation("Creating Private Endpoints Subnet: {SubnetName} with address prefix: {AddressPrefix} for CorrelationId: {CorrelationId}",
                subnetName, settings.Network.PrivateEndpointsSubnet, correlationId);

            var subnet = new Subnet(subnetName, new SubnetArgs
            {
                SubnetName = subnetName,
                ResourceGroupName = resourceGroup,
                VirtualNetworkName = vnet.Name,
                AddressPrefix = settings.Network.PrivateEndpointsSubnet,
                PrivateEndpointNetworkPolicies = DeploymentConstants.Network.DisabledNetworkPolicies
            }, ComponentResourceScope.CreateChildOptions(subnetName));

            _logger.LogDebug("Private Endpoints Subnet {SubnetName} configured with disabled network policies for CorrelationId: {CorrelationId}", subnetName, correlationId);
            return Task.FromResult(subnet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Private Endpoints Subnet for CorrelationId: {CorrelationId}. Error: {ErrorMessage}", correlationId, ex.Message);
            throw;
        }
    }

    private Task<Subnet> CreateApplicationGatewaySubnetAsync(InfrastructureSettings settings, Input<string> resourceGroup, VirtualNetwork vnet, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "Subnet",
            ["SubnetType"] = "ApplicationGateway",
            ["CorrelationId"] = correlationId
        });

        try
        {
            var subnetName = _namingService.GenerateSubnetName(DeploymentConstants.Network.AppGatewaySubnetType, settings.Environment);

            _logger.LogInformation("Creating Application Gateway Subnet: {SubnetName} with address prefix: {AddressPrefix} for CorrelationId: {CorrelationId}", subnetName, settings.Network.ApplicationGatewaySubnet, correlationId);

            var subnet = new Subnet(subnetName, new SubnetArgs
            {
                SubnetName = subnetName,
                ResourceGroupName = resourceGroup,
                VirtualNetworkName = vnet.Name,
                AddressPrefix = settings.Network.ApplicationGatewaySubnet
            }, ComponentResourceScope.CreateChildOptions(subnetName));

            _logger.LogDebug("Application Gateway Subnet {SubnetName} configured for CorrelationId: {CorrelationId}", subnetName, correlationId);
            return Task.FromResult(subnet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Application Gateway Subnet for CorrelationId: {CorrelationId}. Error: {ErrorMessage}", correlationId, ex.Message);
            throw;
        }
    }

    private Task<PrivateDnsZone> CreateContainerAppsPrivateDnsZoneAsync(InfrastructureSettings settings, Input<string> resourceGroup, VirtualNetwork vnet, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "PrivateDnsZone",
            ["DNSZoneType"] = "ContainerApps",
            ["CorrelationId"] = correlationId
        });

        try
        {
            var zoneName = $"{settings.Environment}.{DeploymentConstants.Network.AzureContainerAppsDomain}";

            _logger.LogInformation("Creating Container Apps Private DNS Zone: {ZoneName} for CorrelationId: {CorrelationId}", zoneName, correlationId);

            var privateDnsZone = new PrivateDnsZone(zoneName, new PrivateDnsZoneArgs
            {
                PrivateZoneName = zoneName,
                ResourceGroupName = resourceGroup,
                Location = DeploymentConstants.Network.GlobalLocation,
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.PrivateDnsZoneType, correlationId)
            }, ComponentResourceScope.CreateChildOptions(zoneName));

            // Link the DNS zone to the VNet
            var vnetLinkName = $"{zoneName}{DeploymentConstants.Network.VNetLinkSuffix}";
            new VirtualNetworkLink(vnetLinkName, new VirtualNetworkLinkArgs
            {
                VirtualNetworkLinkName = vnetLinkName,
                ResourceGroupName = resourceGroup,
                PrivateZoneName = privateDnsZone.Name,
                VirtualNetwork = new PrivateDnsSubResourceArgs
                {
                    Id = vnet.Id
                },
                Location = DeploymentConstants.Network.GlobalLocation,
                RegistrationEnabled = false,
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.PrivateDnsZoneLinkType, correlationId)
            }, ComponentResourceScope.CreateChildOptions(vnetLinkName));

            _logger.LogDebug("Container Apps Private DNS Zone {ZoneName} created and linked to VNet for CorrelationId: {CorrelationId}", zoneName, correlationId);
            return Task.FromResult(privateDnsZone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Container Apps Private DNS Zone for CorrelationId: {CorrelationId}. Error: {ErrorMessage}", correlationId, ex.Message);
            throw;
        }
    }

    private Task<PrivateDnsZone> CreateDatabasePrivateDnsZoneAsync(InfrastructureSettings settings, Input<string> resourceGroup, VirtualNetwork vnet, string correlationId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ResourceType"] = "PrivateDnsZone",
            ["DNSZoneType"] = DeploymentConstants.ResourceTags.DatabaseType,
            ["CorrelationId"] = correlationId
        });

        try
        {
            var zoneName = $"{settings.Environment}.{DeploymentConstants.Network.PostgreSQLDomain}";

            _logger.LogInformation("Creating Database Private DNS Zone: {ZoneName} for CorrelationId: {CorrelationId}", zoneName, correlationId);

            var privateDnsZone = new PrivateDnsZone(zoneName, new PrivateDnsZoneArgs
            {
                PrivateZoneName = zoneName,
                ResourceGroupName = resourceGroup,
                Location = DeploymentConstants.Network.GlobalLocation,
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.PrivateDnsZoneType, correlationId)
            }, ComponentResourceScope.CreateChildOptions(zoneName));

            // Link the DNS zone to the VNet
            var vnetLinkName = $"{zoneName}{DeploymentConstants.Network.VNetLinkSuffix}";
            var virtualNetworkLink = new VirtualNetworkLink(vnetLinkName, new VirtualNetworkLinkArgs
            {
                VirtualNetworkLinkName = vnetLinkName,
                ResourceGroupName = resourceGroup,
                PrivateZoneName = privateDnsZone.Name,
                VirtualNetwork = new PrivateDnsSubResourceArgs
                {
                    Id = vnet.Id
                },
                Location = DeploymentConstants.Network.GlobalLocation,
                RegistrationEnabled = false,
                Tags = ResourceTagHelper.GetStandardTags(settings.Environment, DeploymentConstants.ResourceTags.PrivateDnsZoneLinkType, correlationId)
            }, ComponentResourceScope.CreateChildOptions(vnetLinkName));

            _logger.LogDebug("Database Private DNS Zone {ZoneName} created and linked to VNet for CorrelationId: {CorrelationId}", zoneName, correlationId);
            return Task.FromResult(privateDnsZone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Database Private DNS Zone for CorrelationId: {CorrelationId}. Error: {ErrorMessage}", correlationId, ex.Message);
            throw;
        }
    }

    Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup, CancellationToken cancellationToken)
    {
        var correlationId = _correlationIdService.GetOrGenerateCorrelationId();

        throw new ResourceCreationException(
            $"Failed to create network infrastructure for environment: {settings.Environment}. NetworkService requires specific dependencies to be provided.",
            null,
            "Network",
            settings.Environment,
            correlationId,
            ServiceConstants.ErrorCodes.NetworkCreationFailed);
    }

    Task<object> IInfrastructureService.CreateAsync(InfrastructureSettings settings, Input<string> resourceGroup)
    {
        var correlationId = _correlationIdService.GetOrGenerateCorrelationId();

        throw new ResourceCreationException(
            $"Failed to create network infrastructure for environment: {settings.Environment}. NetworkService requires specific dependencies to be provided.",
            null,
            "Network",
            settings.Environment,
            correlationId,
            ServiceConstants.ErrorCodes.NetworkCreationFailed);
    }
}
