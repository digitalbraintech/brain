using DigitalBrain.Tests.Ui;

namespace DigitalBrain.Tests.Authoring;

public class StarterBundleTests
{
    [Fact]
    public void Starter_ask_hop_renders_input_and_button()
    {
        using var harness = new BundleHarness(
            StarterBundleSource.Code, StarterBundleSource.Pack, StarterBundleSource.ExperienceId);

        var tree = harness.GetTree(StarterBundleSource.Hops.Ask);

        tree.ShouldHaveNodeOfType(DigitalBrain.Core.Ui.TextField);
        tree.ShouldHaveButtonWithLabel("Echo");
    }
}
