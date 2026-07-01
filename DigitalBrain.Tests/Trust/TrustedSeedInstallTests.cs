using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Trust;

public class TrustedSeedInstallTests
{
    private sealed class StrictConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            new NeuronTestSiloConfigurator().Configure(siloBuilder);
            siloBuilder.ConfigureServices(services =>
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "true"
                    })
                    .Build();
                services.AddSingleton<IConfiguration>(configuration);
            });
        }
    }

    [Fact]
    public void Trusted_Publisher_Signs_Seeds_So_They_Verify()
    {
        var signed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
        var pack = new NeuroPack(signed.PackName, signed.Version, signed.OwnerId, signed.IsPrivate,
            signed.CommissionRate, signed.Code, signed.Description, signed.AuthorPublicKeyBase64, signed.SignatureBase64, signed.Price);
        Assert.True(PackSignatureVerifier.VerifyPack(pack));
    }

    [Fact]
    public async Task Under_Strict_Default_Signed_Seed_Installs_But_Unsigned_Is_Rejected()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<StrictConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-trusted");

            var seed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
            await market.FireAsync(seed);
            await market.FireAsync(new InstallFromMarketplace(seed.PackName, seed.Version, "buyer"));

            await market.FireAsync(new PublishToMarketplace("UnsignedPack", "1.0", Code: "public class U {}", OwnerId: "stranger"));
            await market.FireAsync(new InstallFromMarketplace("UnsignedPack", "1.0", "buyer"));

            var installed = (await market.GetTimelineAsync()).OfType<NeuroPackInstalled>().Select(i => i.Pack.Name).ToArray();
            Assert.Contains(seed.PackName, installed);
            Assert.DoesNotContain("UnsignedPack", installed);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
