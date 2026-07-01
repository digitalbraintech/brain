using DigitalBrain.Core;

namespace DigitalBrain.Tests.Ui;

public class MarketplaceFacetTests
{
    private static NeuroPack Pack(string name, BundleTier tier, params BundleChannel[] channels) =>
        new(name, "1.0.0", Manifest: new BundleManifest(tier, new ExperienceRef(name), channels));

    private static IEnumerable<UiWidgetTree> Descend(UiWidgetTree n)
    {
        yield return n;
        if (n.Children is null) yield break;
        foreach (var c in n.Children)
            foreach (var d in Descend(c)) yield return d;
    }

    [Fact]
    public void Tree_has_facet_buttons_for_all_and_each_distinct_tier_and_channel()
    {
        var packs = new[]
        {
            Pack("a", BundleTier.Content, BundleChannel.InApp),
            Pack("b", BundleTier.Substrate, BundleChannel.Telegram),
        };

        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            packs, Array.Empty<NeuroPack>(), tierFilter: null, channelFilter: null, emitter: "market-main");

        var tree = (UiWidgetTree)surface.Props["tree"]!;
        var buttons = Descend(tree)
            .Where(n => n.Type == DigitalBrain.Core.NeuronUiKit.ActionButton)
            .Select(n => n.Props[UiSurfaceKeys.Label]?.ToString())
            .ToList();

        Assert.Contains("All", buttons);
        Assert.Contains("Content", buttons);
        Assert.Contains("Substrate", buttons);
        Assert.Contains("Telegram", buttons);
    }

    [Fact]
    public void Facet_button_fires_FilterMarketplace_with_its_tier()
    {
        var packs = new[] { Pack("a", BundleTier.Content, BundleChannel.InApp) };

        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            packs, Array.Empty<NeuroPack>(), null, null, "market-main");

        var tree = (UiWidgetTree)surface.Props["tree"]!;
        var contentBtn = Descend(tree).Single(n =>
            n.Type == DigitalBrain.Core.NeuronUiKit.ActionButton
            && n.Props[UiSurfaceKeys.Label]?.ToString() == "Content");

        Assert.Equal(nameof(FilterMarketplace), contentBtn.Props[UiSurfaceKeys.SynapseType]);
        var btnProps = (IReadOnlyDictionary<string, object?>)contentBtn.Props[UiSurfaceKeys.Props]!;
        Assert.Equal("Content", btnProps["tier"]);
    }

    [Fact]
    public void Tier_filter_restricts_the_list_items()
    {
        var packs = new[]
        {
            Pack("a", BundleTier.Content, BundleChannel.InApp),
            Pack("b", BundleTier.Substrate, BundleChannel.InApp),
        };

        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            packs, Array.Empty<NeuroPack>(), tierFilter: "Content", channelFilter: null, emitter: "market-main");

        var items = (Dictionary<string, object?>[])surface.Props["packs"]!;
        Assert.Single(items);
        Assert.Equal("a", items[0]["name"]);
    }
}
