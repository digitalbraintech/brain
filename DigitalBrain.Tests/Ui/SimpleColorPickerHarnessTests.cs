using DigitalBrain.Core;

namespace DigitalBrain.Tests.Ui;

// Fast, in-memory tests over the SHIPPED bundle source (single source of truth) via BundleHarness.
// No browser, no full stack — millisecond feedback. This is the primary daily authoring loop.
public class UiTestingFrameworkExamples
{
    [Fact]
    public void Can_drive_hello_world_experience_and_assert_tree()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var tree = harness.GetTree("ask");

        tree.ShouldHaveNodeOfType(DigitalBrain.Core.Ui.TextField);
        tree.ShouldHaveButtonWithLabel("Greet");
    }

    [Fact]
    public void Can_use_golden_snapshot_and_matchers()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var tree = harness.GetTree("ask");

        var snapshot = tree.ToGoldenSnapshot();
        Assert.Contains("ui:TextField", snapshot);

        tree.ShouldHaveButtonWithLabel("Greet");
    }
}
