using DigitalBrain.Core;
using DigitalBrain.Tests.E2E.Packs;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class HelloWorldRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task HelloWorld_asks_for_name_then_greets()
    {
        E2EPrerequisites.RequireRenderE2E();

        var driver = new LiveRenderVerifier(_fx, pack: "hello-world", experienceId: "hello-world");
        await driver.PublishAndInstallAsync(HelloWorldPackSource.Code, description: "Hello World experience");
        await driver.OpenAsync();

        await driver.SendUserActionAsync("start");
        await driver.AssertSurfaceRenderedAsync(MarketplaceSeeds.HelloWorldHops.Ask);

        await driver.SendUserActionAsync(MarketplaceSeeds.HelloWorldHops.Greeting, ("name", "Alice"));
        await driver.AssertSurfaceRenderedAsync(MarketplaceSeeds.HelloWorldHops.Greeting);

        await _fx.Page.Locator("text=Hello Alice!").WaitForAsync(new() { Timeout = 30_000 });
    }
}
