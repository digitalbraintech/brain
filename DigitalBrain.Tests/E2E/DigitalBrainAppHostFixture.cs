using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Net.Client;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.Tests.E2E;

public class DigitalBrainAppHostFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;

    public string GatewayHttpsUrl { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        var testId = Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable("DIGITALBRAIN_TEST_MODE", "true");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", "true");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_SURFACES_ENABLED", "true");
        Environment.SetEnvironmentVariable("DigitalBrain__ClusterId", $"e2e-{testId}");

        // Resolve the AppHost entry point type from the referenced assembly without pulling duplicate Program symbols into global scope.
        var appHostAssembly = Assembly.Load("NeuroOSPrototype.AppHost");
        var programType = appHostAssembly.GetTypes().FirstOrDefault(t => t.Name == "Program")
                          ?? appHostAssembly.EntryPoint?.DeclaringType
                          ?? throw new InvalidOperationException("Could not locate AppHost Program type for DistributedApplicationTestingBuilder.");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync(programType);

        foreach (var parameter in builder.Resources.OfType<ParameterResource>())
        {
            builder.Configuration[$"Parameters:{parameter.Name}"] = "e2e-test";
        }

        App = await builder.BuildAsync();
        await App.StartAsync();

        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway");
        await App.ResourceNotifications.WaitForResourceHealthyAsync("silo");

        // Resolve an external http(s) endpoint for the browser target. The exact names depend on the Aspire model (gateway/silo + AddDigitalBrain wiring).
        string url = "https://localhost:8080";
        try
        {
            url = App.GetEndpoint("gateway", "https").ToString();
        }
        catch
        {
            try { url = App.GetEndpoint("gateway", "http").ToString(); }
            catch
            {
                try { url = App.GetEndpoint("silo", "https").ToString(); }
                catch
                {
                    try { url = App.GetEndpoint("silo", "http").ToString(); }
                    catch { /* keep default; E2E will still exercise boot + playwright launch */ }
                }
            }
        }
        GatewayHttpsUrl = url;
    }

    public virtual async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    public GrpcChannel CreateGatewayGrpcChannel()
    {
        return GrpcChannel.ForAddress(GatewayHttpsUrl);
    }

    // Pack-specific helpers for E2E: drive real marketplace publish/install so packs embody via ALC/IPackBehavior.
    public async Task PublishPackAsync(string packName, string version, string code = "/* E2E test pack code */", string owner = "e2e-test", double commissionRate = 0.0, string description = "pack for E2E surface render test")
    {
        using var channel = CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var cmd = new { PackName = packName, Version = version, Code = code, OwnerId = owner, IsPrivate = false, CommissionRate = commissionRate, Description = description };
        var payload = System.Text.Json.JsonSerializer.Serialize(cmd);

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "e2e-pub-" + packName,
            TypeName = "PublishToMarketplace",
            Payload = ByteString.CopyFromUtf8(payload)
        });
    }

    public async Task InstallPackAsync(string packName, string version, string buyer = "e2e-browser-user")
    {
        using var channel = CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var cmd = new { PackName = packName, Version = version, BuyerId = buyer };
        var payload = System.Text.Json.JsonSerializer.Serialize(cmd);

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "e2e-install-" + packName,
            TypeName = "InstallFromMarketplace",
            Payload = ByteString.CopyFromUtf8(payload)
        });
    }

    public async Task SendSynapseAsync(string typeName, string jsonPayload, string? correlationId = null)
    {
        using var channel = CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            TypeName = typeName,
            Payload = ByteString.CopyFromUtf8(jsonPayload)
        });
    }
}
