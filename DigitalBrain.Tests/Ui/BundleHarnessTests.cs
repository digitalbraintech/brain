using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class BundleHarnessTests
{
    [Fact]
    public void Drives_shipped_hello_world_source_and_asserts_ask_hop_tree()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var tree = harness.GetTree("ask");

        tree.ShouldHaveNodeOfType(DigitalBrain.Core.Ui.TextField);
        tree.ShouldHaveButtonWithLabel("Greet");
    }
}
