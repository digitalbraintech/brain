using DigitalBrain.Core;
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
        var verifier = new LiveRenderVerifier(_fx, pack: "ui-gallery", experienceId: "ui-gallery");
        await verifier.OpenAsync();

        // Client auto-starts the experience for the preinstalled gallery seed
        await verifier.AssertSurfaceRenderedAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Inputs);

        await verifier.SendUserActionAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Display);
        await verifier.AssertSurfaceRenderedAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Display);

        await verifier.SendUserActionAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Feedback);
        await verifier.AssertSurfaceRenderedAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Feedback);

        await verifier.SendUserActionAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Navigation);
        await verifier.AssertSurfaceRenderedAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Navigation);

        await verifier.SendUserActionAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Overlays);
        await verifier.AssertSurfaceRenderedAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Overlays);
    }

    [SkippableFact]
    public async Task Gallery_auto_starts_from_the_client_without_a_manual_step()
    {
        E2EPrerequisites.RequireRenderE2E();

        // Reproduces the real desktop flow: the ExperienceHostScreen must auto-fire ExperienceStep(start)
        // itself (no test-sent step), then the first hop must render.
        var verifier = new LiveRenderVerifier(_fx, pack: "ui-gallery", experienceId: "ui-gallery");
        await verifier.OpenAsync();

        await verifier.AssertSurfaceRenderedAsync(DigitalBrain.Core.MarketplaceSeeds.UiGalleryHops.Inputs);
    }
}
