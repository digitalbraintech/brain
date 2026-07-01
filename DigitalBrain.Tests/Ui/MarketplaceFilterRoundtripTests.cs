using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class MarketplaceFilterRoundtripTests
{
    [Fact]
    public async Task Filtering_by_tier_reemits_a_surface_listing_only_matching_bundles()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-facet-1");
            // hello-world is a KitExperience → Content tier when materialized at publish.
            await market.FireAsync(new PublishToMarketplace(
                "hello-world", "1.0.0", Code: MarketplaceSeeds.HelloWorldPackCode, OwnerId: "tester", CommissionRate: 0.0));
            // a plain behavior pack → no manifest → not Content.
            await market.FireAsync(new PublishToMarketplace(
                "plain", "1.0.0", Code: "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }",
                OwnerId: "tester", CommissionRate: 0.0));

            await market.FireAsync(new FilterMarketplace(Tier: "Content"));

            var surface = (await market.GetTimelineAsync())
                .OfType<UiSurface>()
                .Last(s => s.Kind == UiSurfaceKinds.MarketplaceList);
            var items = (System.Collections.Generic.Dictionary<string, object?>[])surface.Props["packs"]!;

            Assert.Contains(items, i => i["name"]?.ToString() == "hello-world");
            Assert.DoesNotContain(items, i => i["name"]?.ToString() == "plain");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }
}
