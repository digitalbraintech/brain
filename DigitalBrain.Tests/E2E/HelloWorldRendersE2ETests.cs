using DigitalBrain.Tests.E2E.Packs;
using Xunit;

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

        var driver = new ExperienceFlowDriver(_fx, pack: "hello-world", experienceId: "hello-world");
        await driver.PublishAndInstallAsync(HelloWorldPackSource.Code, description: "Hello World experience");
        await driver.OpenAsync();

        await driver.TriggerExperienceAsync();
        await driver.AssertHopRendersAsync("ask");

        await driver.TapAsync("greeting", ("name", "Alice"));
        await driver.AssertHopRendersAsync("greeting");

        await _fx.Page.Locator("text=Hello Alice!").WaitForAsync(new() { Timeout = 30_000 });
    }
}
