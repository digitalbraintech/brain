using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class UiGalleryRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task Gallery_opens_and_walks_category_hops()
    {
        E2EPrerequisites.RequireRenderE2E();

        // No explicit install: ui-gallery is a preinstalled local seed shown in the installer, so opening it
        // must start the experience directly (it is embodied lazily from the seed catalog on first use).
        var driver = new ExperienceFlowDriver(_fx, pack: "ui-gallery", experienceId: "ui-gallery");
        await driver.OpenAsync();

        await driver.TriggerExperienceAsync();
        await driver.AssertHopRendersAsync("inputs");

        await driver.TapAsync("display");
        await driver.AssertHopRendersAsync("display");

        await driver.TapAsync("feedback");
        await driver.AssertHopRendersAsync("feedback");

        await driver.TapAsync("navigation");
        await driver.AssertHopRendersAsync("navigation");

        await driver.TapAsync("overlays");
        await driver.AssertHopRendersAsync("overlays");
    }

    [SkippableFact]
    public async Task Gallery_auto_starts_from_the_client_without_a_manual_step()
    {
        E2EPrerequisites.RequireRenderE2E();

        // Reproduces the real desktop flow: the ExperienceHostScreen must auto-fire ExperienceStep(start)
        // itself (no test-sent step), then the first hop must render.
        var driver = new ExperienceFlowDriver(_fx, pack: "ui-gallery", experienceId: "ui-gallery");
        await driver.OpenAsync();

        await driver.AssertHopRendersAsync("inputs");
    }
}
