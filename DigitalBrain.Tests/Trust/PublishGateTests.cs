using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Trust;

public class PublishGateTests
{
    private const string PackCode =
        "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }";

    // Single configurator: calls NeuronTestSiloConfigurator inline, then overrides IConfiguration with
    // GatePublishing=true. This mirrors the TrustedSeedInstallTests pattern where the override registration
    // is guaranteed to be the last AddSingleton<IConfiguration> in the same ConfigureServices chain.
    private sealed class GatedSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            new NeuronTestSiloConfigurator().Configure(siloBuilder);
            siloBuilder.ConfigureServices(services =>
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false",
                        ["DigitalBrain:Marketplace:GatePublishing"] = "true"
                    })
                    .Build();
                services.AddSingleton<IConfiguration>(configuration);
            });
        }
    }

    private static TestCluster GatedCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<GatedSiloConfigurator>();
        return builder.Build();
    }

    private static async Task<IReadOnlyList<NeuroPack>> ListedAsync(IMarketplaceNeuron market)
    {
        await market.FireAsync(new ListPublished());
        return (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
    }

    [Fact]
    public async Task Gate_on_admits_a_trusted_publisher()
    {
        var cluster = GatedCluster();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-gate-trusted");
            var signed = TrustedPublisher.SignPublishCommand(
                new PublishToMarketplace("trusted-pack", "1.0.0", Code: PackCode));
            await market.FireAsync(signed);

            Assert.Contains(await ListedAsync(market), p => p.Name == "trusted-pack");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Gate_on_rejects_a_stranger()
    {
        var cluster = GatedCluster();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-gate-stranger");
            var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
            var stranger = PackSignatureVerifier.SignPack(
                new NeuroPack("stranger-pack", "1.0.0", Code: PackCode), priv, pub);
            await market.FireAsync(new PublishToMarketplace(
                stranger.Name, stranger.Version, Code: stranger.Code,
                AuthorPublicKeyBase64: stranger.AuthorPublicKeyBase64, SignatureBase64: stranger.SignatureBase64));

            Assert.DoesNotContain(await ListedAsync(market), p => p.Name == "stranger-pack");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Gate_off_by_default_admits_unsigned()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-gate-off");
            await market.FireAsync(new PublishToMarketplace("unsigned-pack", "1.0.0", Code: PackCode));

            Assert.Contains(await ListedAsync(market), p => p.Name == "unsigned-pack");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }
}
