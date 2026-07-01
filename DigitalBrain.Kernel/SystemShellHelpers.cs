using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

public static class SystemShellHelpers
{
    public static IEnumerable<UiWidgetTree> BuildShellMenuItems()
    {
        var items = new List<(string Label, string? TargetSurfaceKind, IReadOnlyDictionary<string, object?>? Action)>
        {
            ("Marketplace", UiSurfaceKinds.MarketplaceList, null),
            ("Installed", UiSurfaceKinds.InstalledBundles, null),
            ("SE Hello World", "hello-world-se", null),
            ("Tasks", UiSurfaceKinds.TaskManager, null),
            ("INO Chat", "chat", null),
            ("Timeline", UiSurfaceKinds.Timeline, null)
        };
        foreach (var seed in MarketplaceSeeds.LocalUiPacks.Where(p => p.Name.StartsWith("DigitalBrain.UI")).Take(1))
        {
            items.Add(($"Open {seed.Name}", "marketplace-list", null));
        }
        foreach (var (label, target, action) in items)
        {
            var itemProps = new Dictionary<string, object?> { ["label"] = label };
            if (action != null) itemProps["action"] = action;
            else if (target != null) itemProps["targetSurfaceKind"] = target;
            yield return new(NeuronUiKit.MenuItem, itemProps);
        }
        yield return new(NeuronUiKit.Divider, new Dictionary<string, object?>());
    }
}
