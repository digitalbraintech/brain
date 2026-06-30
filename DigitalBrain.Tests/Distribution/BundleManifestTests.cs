using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class BundleManifestTests
{
    [Fact]
    public void Plain_pack_has_no_bundle_manifest_by_default()
    {
        IPackBehavior pack = new BarePack();
        Assert.Null(pack.GetBundleManifest());
    }

    [Fact]
    public void Kit_experience_reports_content_tier_inapp_channel_and_entry_experience()
    {
        IPackBehavior pack = new GreetExperience();

        var manifest = pack.GetBundleManifest();

        Assert.NotNull(manifest);
        Assert.Equal(BundleTier.Content, manifest!.Tier);
        Assert.Equal("greet", manifest.EntryExperience?.ExperienceId);
        Assert.Contains(BundleChannel.InApp, manifest.Channels);
    }

    private sealed class BarePack : IPackBehavior
    {
        public string Respond(string input) => input;
    }

    private sealed class GreetExperience : KitExperience
    {
        protected override UiExperience Define() => Experience("greet", "Greet")
            .Hop("ask", s => s.Text("hi").Button("Go", "done"))
            .Hop("done", s => s.Text("done"));
    }
}
