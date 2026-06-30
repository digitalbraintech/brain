using DigitalBrain.Core;
using DigitalBrain.Tests.E2E.Packs;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class SimpleColorPickerRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task SimpleColorPicker_chooses_and_shows_result()
    {
        E2EPrerequisites.RequireRenderE2E();

        var driver = new LiveRenderVerifier(_fx, pack: "simple-color-picker", experienceId: "simple-color-picker");
        await driver.PublishAndInstallAsync(SimpleColorPickerPackSource.Code, description: "Simple color picker experience");
        await driver.OpenAsync();

        await driver.SendUserActionAsync("start");
        await driver.AssertSurfaceRenderedAsync(MarketplaceSeeds.SimpleColorPickerHops.Choose);

        await driver.SendUserActionAsync(MarketplaceSeeds.SimpleColorPickerHops.Result, ("color", "Green"));
        await driver.AssertSurfaceRenderedAsync(MarketplaceSeeds.SimpleColorPickerHops.Result);

        await _fx.Page.Locator("text=You chose: Green").WaitForAsync(new() { Timeout = 30_000 });
    }
}
