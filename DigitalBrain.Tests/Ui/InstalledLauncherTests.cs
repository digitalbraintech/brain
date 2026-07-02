using DigitalBrain.Core;

namespace DigitalBrain.Tests.Ui;

public class InstalledLauncherTests
{
    private static IEnumerable<UiWidgetTree> Buttons(UiWidgetTree node)
    {
        if (node.Type == "fbutton") yield return node;
        foreach (var child in node.Children ?? new List<UiWidgetTree>())
            foreach (var button in Buttons(child))
                yield return button;
    }

    private static List<UiWidgetTree> BundleButtons(string packName, params NeuroPack[] packs)
    {
        var surface = UiSurfaceLiveData.InstalledBundlesFromPacks(packs, packs);
        var tree = (UiWidgetTree)surface.Props["tree"]!;
        return Buttons(tree)
            .Where(b => b.Props.TryGetValue("packName", out var pn) && pn?.ToString() == packName)
            .ToList();
    }

    // Experience pack open button test for deleted demo (ui-gallery) removed.
    // The generic open button behavior is tested in the next test.

    [Fact]
    public void Pack_Without_An_Open_Experience_Keeps_The_Generic_Open_Button()
    {
        var plain = new NeuroPack("plain-launch-pack", "1.0.0");

        var buttons = BundleButtons("plain-launch-pack", plain);

        // Default experiences are "Run"/"Emit" (not "Open"), so the generic launcher Open stays as the entry point.
        Assert.Contains(buttons, b =>
            b.Props.TryGetValue("targetSurfaceKind", out var t) && t?.ToString() == "plain-launch-pack");
    }
}
