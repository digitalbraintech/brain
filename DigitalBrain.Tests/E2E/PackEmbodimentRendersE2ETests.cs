using Microsoft.Playwright;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Trait("Group", "Flutter")]
[Trait("Group", "Marketplace")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class PackEmbodimentRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    // IAW-style separate groups: flutter (this pack E2E), telegram (Telegram tests), google/llm (Llm tests), windows/fs (Sdk/Filesystem tests) - testable independently.
    private readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task InstallsRealPack_EmbodiedCode_RendersSurface_ObservedInFlutter()
    {
        E2EPrerequisites.RequireRenderE2E();

        const string packName = "E2ESurfacePack";
        const string version = "1.0";
        const string surfaceId = "pack-surface-e2e";

        await _fx.PublishPackAsync(packName, version,
            code: TestPacks.RenderableSurfacePack(surfaceId),
            description: "E2E pack that emits a renderable surface");
        await _fx.InstallPackAsync(packName, version, buyer: "e2e-ui-watcher");

        // SurfaceDemoRequested with correlationId == surfaceId so the observability
        // surface cards carry that exact id as their RfwCard.CorrelationId.
        await _fx.SendSynapseAsync(
            "DigitalBrain.Kernel.SurfaceDemoRequested",
            $"{{\"source\":\"{surfaceId}\"}}",
            correlationId: surfaceId);

        await _fx.Page.GotoAsync(_fx.GatewayHttpsUrl, new() { WaitUntil = WaitUntilState.Load });

        var node = _fx.Page.Locator($"[flt-semantics-identifier=\"{surfaceId}\"]");
        await node.WaitForAsync(new() { Timeout = 30_000 });
        Assert.Equal(1, await node.CountAsync());

        // Real browser assert for routed surface id (via flt-semantics-identifier attr) + context readiness.
        await _fx.AssertSurfaceContext($"[flt-semantics-identifier=\"{surfaceId}\"]", "surfaceId", surfaceId);

        var shot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"e2e-render-{surfaceId}.png");
        await _fx.Page.ScreenshotAsync(new() { Path = shot });
    }
}
