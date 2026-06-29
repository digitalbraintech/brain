using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Net.Client;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace DigitalBrain.Tests.E2E;

public class DigitalBrainAppHostFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;

    // The browser navigates here: the kernel "web" endpoint (Http1AndHttp2) serves the Flutter
    // bundle and gRPC-Web. Native gRPC helpers must NOT use this — over cleartext an Http1AndHttp2
    // endpoint answers HTTP/1.1 (no ALPN), so an HTTP/2 gRPC call gets HTTP_1_1_REQUIRED.
    public string GatewayHttpsUrl { get; private set; } = null!;

    // The native-gRPC helpers dial here: the kernel "grpc" endpoint (Http2-only).
    public string GrpcUrl { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        if (!(E2EPrerequisites.OptedIn && E2EPrerequisites.WebBundlePresent))
            return; // Prereqs absent: the [SkippableFact] will skip; don't boot the AppHost.

        var testId = Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable("DIGITALBRAIN_TEST_MODE", "true");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", "true");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_SURFACES_ENABLED", "true");
        Environment.SetEnvironmentVariable("DigitalBrain__ClusterId", $"e2e-{testId}");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_KERNEL_REPLICAS",
            Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_REPLICAS") ?? "1");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_WEBROOT", E2EPrerequisites.WebBundleDir);

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

        using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await App.ResourceNotifications.WaitForResourceHealthyAsync("kernel", startupCts.Token);

        // Browser nav target: the kernel "web" endpoint (static bundle + gRPC-Web).
        // The standalone gateway remains an optional diagnostic resource for legacy smoke tests.
        string webUrl = "https://localhost:8080";
        try { webUrl = App.GetEndpoint("kernel", "web").ToString(); }
        catch
        {
            try { webUrl = App.GetEndpoint("gateway", "https").ToString(); }
            catch { try { webUrl = App.GetEndpoint("gateway", "http").ToString(); } catch { } }
        }
        GatewayHttpsUrl = webUrl;

        // Native-gRPC target: the kernel "grpc" endpoint (Http2). Falls back to the web URL only
        // if "grpc" is unavailable (legacy gateway smoke tests).
        string grpcUrl = webUrl;
        try { grpcUrl = App.GetEndpoint("kernel", "grpc").ToString(); } catch { }
        GrpcUrl = grpcUrl;
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
        return GrpcChannel.ForAddress(GrpcUrl);
    }

    // Pack-specific helpers for E2E: drive real marketplace publish/install so packs embody via ALC/IPackBehavior.
    public async Task PublishPackAsync(string packName, string version, string code = "/* E2E test pack code */", string owner = "e2e-test", double commissionRate = 0.0, string description = "pack for E2E surface render test")
    {
        using var channel = CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        // Sign the pack so it passes the install-time RejectUnsignedPacks gate (the secure default).
        // Self-signed is sufficient: the gate verifies code integrity, not publisher identity.
        var (privateKey, publicKey) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(
            new NeuroPack(packName, version, owner, false, commissionRate, code, description), privateKey, publicKey);

        var cmd = new
        {
            PackName = packName, Version = version, Code = code, OwnerId = owner,
            IsPrivate = false, CommissionRate = commissionRate, Description = description,
            AuthorPublicKeyBase64 = signed.AuthorPublicKeyBase64, SignatureBase64 = signed.SignatureBase64
        };
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

    public async Task SendExperienceStepAsync(string pack, string experienceId, string eventName, IReadOnlyDictionary<string, string>? args = null)
    {
        using var channel = CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var payload = new Dictionary<string, string>(args ?? new Dictionary<string, string>())
        {
            ["pack"] = pack,
            ["experienceId"] = experienceId,
            ["eventName"] = eventName,
        };

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "e2e-step-" + eventName,
            TypeName = nameof(ExperienceStep),
            Payload = ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(payload))
        });
    }
}
