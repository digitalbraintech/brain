using DigitalBrain.Tests.E2E.Packs;
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

        var driver = new ExperienceFlowDriver(_fx, pack: "ui-gallery", experienceId: "ui-gallery");
        await driver.PublishAndInstallAsync(UiGalleryPackSource.Code, description: "UI Kit Gallery experience");
        await driver.OpenAsync();

        await driver.TriggerExperienceAsync();
        await driver.AssertHopRendersAsync("inputs");

        await driver.TapAsync("display");
        await driver.AssertHopRendersAsync("display");

        await driver.TapAsync("feedback");
        await driver.AssertHopRendersAsync("feedback");
    }
}
