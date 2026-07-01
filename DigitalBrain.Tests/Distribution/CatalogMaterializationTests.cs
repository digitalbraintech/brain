using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class CatalogMaterializationTests
{
    [Fact]
    public async Task Publishing_a_kit_bundle_materializes_its_manifest_into_the_catalog()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-catalog-1");
            await market.FireAsync(new PublishToMarketplace(
                "hello-world", "1.0.0", Code: MarketplaceSeeds.HelloWorldPackCode, OwnerId: "tester", CommissionRate: 0.0));
            await market.FireAsync(new ListPublished());

            var listed = (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
            var hello = listed.Single(p => p.Name == "hello-world");

            Assert.NotNull(hello.Manifest);
            Assert.Equal(BundleTier.Content, hello.Manifest!.Tier);
            Assert.Equal("hello-world", hello.Manifest.EntryExperience?.ExperienceId);
            Assert.Contains(BundleChannel.InApp, hello.Manifest.Channels);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
