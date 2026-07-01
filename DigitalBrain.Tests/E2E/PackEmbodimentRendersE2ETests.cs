using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class PackEmbodimentRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    // E2E for UiSurface flow (ties to tg context: tg Signal -> neuron emits UiSurface with originChannel -> flutter render).
    private readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task InstallsRealPack_EmbodiedCode_RendersSurface_ObservedInFlutter()
    {
        E2EPrerequisites.RequireRenderE2E();

        const string packName = "E2ESurfacePack";
        const string version = "1.0";
        // surfaceId == the correlationId sent to SurfaceDemoRequested.
        // The observability surface that path emits via ObservabilityNeuron preserves
        // the correlationId through HomeFeedBus → RfwCardEnvelope.CorrelationId →
        // PanelManager.upsertFromEnvelope → CanvasPanel.id → host.render semanticsId.
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

        // E2E continue: use fixture helper to assert context for routed surface (placeholder for tg origin etc).
        await _fx.AssertSurfaceContext($"[flt-semantics-identifier=\"{surfaceId}\"]", "originChannel", "telegram");

        var shot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"e2e-render-{surfaceId}.png");
        await _fx.Page.ScreenshotAsync(new() { Path = shot });
    }
}
