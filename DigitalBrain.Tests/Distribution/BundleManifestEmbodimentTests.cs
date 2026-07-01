using DigitalBrain.Core;
using DigitalBrain.Tests.Ui;

namespace DigitalBrain.Tests.Distribution;

public class BundleManifestEmbodimentTests
{
    [Fact]
    public void Embodied_hello_world_surfaces_its_bundle_manifest()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var manifest = harness.Manifest;

        Assert.NotNull(manifest);
        Assert.Equal(BundleTier.Content, manifest!.Tier);
        Assert.Equal("hello-world", manifest.EntryExperience?.ExperienceId);
        Assert.Contains(BundleChannel.InApp, manifest.Channels);
    }
}
