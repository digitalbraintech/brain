using DigitalBrain.Tests.Authoring;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class StarterBundleRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task Starter_asks_then_echoes()
    {
        E2EPrerequisites.RequireRenderE2E();

        var driver = new LiveRenderVerifier(
            _fx, pack: StarterBundleSource.Pack, experienceId: StarterBundleSource.ExperienceId);
        await driver.PublishAndInstallAsync(StarterBundleSource.Code, description: "Starter bundle");
        await driver.OpenAsync();

        await driver.SendUserActionAsync("start");
        await driver.AssertSurfaceRenderedAsync(StarterBundleSource.Hops.Ask);

        await driver.SendUserActionAsync(StarterBundleSource.Hops.Result, ("message", "ping"));
        await driver.AssertSurfaceRenderedAsync(StarterBundleSource.Hops.Result);

        await _fx.Page.Locator("text=You said: ping").WaitForAsync(new() { Timeout = 30_000 });
    }
}
