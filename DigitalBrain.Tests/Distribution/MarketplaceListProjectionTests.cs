using DigitalBrain.Core;

namespace DigitalBrain.Tests.Distribution;

public class MarketplaceListProjectionTests
{
    [Fact]
    public void Marketplace_list_projects_bundle_manifest_facets()
    {
        var manifest = new BundleManifest(
            BundleTier.Content, new ExperienceRef("greet"), new[] { BundleChannel.InApp });
        var pack = new NeuroPack("greet", "1.0.0", Manifest: manifest);

        var surface = UiSurfaceLiveData.MarketplaceListFromPacks(new[] { pack }, Array.Empty<NeuroPack>());

        var items = (Dictionary<string, object?>[])surface.Props["packs"]!;
        var item = items.Single();
        Assert.Equal("Content", item["tier"]);
        Assert.Equal("greet", item["entryExperienceId"]);
        Assert.Contains("InApp", (string[])item["channels"]!);
    }

    [Fact]
    public void Marketplace_list_leaves_facets_null_for_packs_without_a_manifest()
    {
        var pack = new NeuroPack("plain", "1.0.0");

        var surface = UiSurfaceLiveData.MarketplaceListFromPacks(new[] { pack }, Array.Empty<NeuroPack>());

        var item = ((Dictionary<string, object?>[])surface.Props["packs"]!).Single();
        Assert.Null(item["tier"]);
        Assert.Null(item["channels"]);
        Assert.Null(item["entryExperienceId"]);
    }
}
