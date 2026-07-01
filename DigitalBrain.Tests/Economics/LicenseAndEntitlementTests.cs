using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Economics;

public class LicenseAndEntitlementTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task License_Issues_Verifies_And_Gates_Entitlement()
    {
        var license = _cluster!.GrainFactory.GetGrain<ILicenseNeuron>("license-main");
        var token = await license.IssueLicenseAsync("PackA", "buyer1");

        Assert.True(await license.HasLicenseAsync("PackA", "buyer1"));
        Assert.False(await license.HasLicenseAsync("PackA", "stranger"));

        Assert.True(await license.VerifyLicenseAsync(token, "PackA", "buyer1"));
        Assert.False(await license.VerifyLicenseAsync(token, "PackA", "stranger")); // payload mismatch
        Assert.False(await license.VerifyLicenseAsync("not-a-token", "PackA", "buyer1")); // malformed
    }

    [Fact]
    public async Task Premium_Pack_Install_Requires_A_License()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var premium = PackSignatureVerifier.SignPack(
            new NeuroPack("Premium", "1.0", OwnerId: "dev", Code: "ok", Price: 9.99m), priv, pub);

        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-premium");
        await Publish(market, premium);

        // No license -> the premium gate rejects the install.
        await market.FireAsync(new InstallFromMarketplace("Premium", "1.0", BuyerId: "buyer1"));
        Assert.DoesNotContain(await market.GetTimelineAsync(), s => s is NeuroPackInstalled);

        // Grant a license -> install succeeds.
        await _cluster.GrainFactory.GetGrain<ILicenseNeuron>("license-main").IssueLicenseAsync("Premium", "buyer1");
        await market.FireAsync(new InstallFromMarketplace("Premium", "1.0", BuyerId: "buyer1"));
        Assert.Contains(await market.GetTimelineAsync(), s => s is NeuroPackInstalled);
    }

    [Fact]
    public async Task Full_Purchase_Flow_Synthetic_Issues_License_And_Installs()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var premium = PackSignatureVerifier.SignPack(
            new NeuroPack("FlowPack", "1.0", OwnerId: "dev", Code: "ok", Price: 5m), priv, pub);

        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-flow");
        await Publish(market, premium);

        // Buyer pays via the synthetic gateway.
        var gateway = new SyntheticPaymentGateway();
        var session = await gateway.CreateCheckoutAsync(new CheckoutRequest("FlowPack", "buyerX", "FlowPack", 5m, "s", "c"));
        var confirmation = gateway.VerifyWebhook(
            JsonSerializer.Serialize(new { sessionId = session.SessionId, bundleId = "FlowPack", userId = "buyerX" }), null);
        Assert.True(confirmation.Completed);

        // Payment confirmed -> issue license -> install the premium pack.
        await _cluster.GrainFactory.GetGrain<ILicenseNeuron>("license-main")
            .IssueLicenseAsync(confirmation.BundleId!, confirmation.UserId!);
        await market.FireAsync(new InstallFromMarketplace("FlowPack", "1.0", BuyerId: "buyerX"));

        Assert.Contains(await market.GetTimelineAsync(), s => s is NeuroPackInstalled);
    }

    private static Task Publish(IMarketplaceNeuron market, NeuroPack pack) =>
        market.FireAsync(new PublishToMarketplace(
            pack.Name, pack.Version, pack.Code, pack.OwnerId, pack.IsPrivate, pack.CommissionRate,
            pack.Description, pack.AuthorPublicKeyBase64, pack.SignatureBase64, pack.Price)).AsTask();
}

