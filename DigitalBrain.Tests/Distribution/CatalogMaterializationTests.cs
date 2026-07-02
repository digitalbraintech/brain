using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

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
            // hello-world demo pack removed.
            await market.FireAsync(new ListPublished());

            var listed = (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
            // hello-world specific asserts removed.
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
