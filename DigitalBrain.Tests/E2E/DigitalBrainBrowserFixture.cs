using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

/// <summary>
/// Provides a real running Flutter web app (inside a Chromium via Playwright) against
/// the full DigitalBrain stack (Aspire + kernels + gateway).
///
/// This fixture exists **only** for live rendering verification:
/// - Does the RFW + UiSurfaceTreeRenderer + ui_kit actually produce visible, accessible widgets?
/// - Do screenshots / semantics look correct?
///
/// User actions (button taps, form submits) are sent as <see cref="ExperienceStep"/> synapses
/// (via <see cref="LiveRenderVerifier"/> / <see cref="ExperienceFlowDriver"/>).
/// This is intentional and correct for a neuron/synapse architecture.
///
/// For fast iteration use:
/// - <see cref="ExperienceTestHarness{T}"/> (in-memory model + tree assertions)
/// - Flutter widget tests against the renderer
/// - The existing KitExperience unit tests
///
/// Only bring up this fixture when you need end-to-end visual + stack confirmation.
/// </summary>
public class DigitalBrainBrowserFixture : DigitalBrainAppHostFixture
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        if (App is null) return;

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        bool isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        bool forceHeaded = string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADED"), "true", StringComparison.OrdinalIgnoreCase);
        bool forceHeadless = string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADLESS"), "true", StringComparison.OrdinalIgnoreCase);
        bool headless = forceHeaded ? false : (forceHeadless || isCi);
        float? slowMo = int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_SLOWMO"), out var ms) ? ms : null;

        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            SlowMo = slowMo,
        });

        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });

        Page = await context.NewPageAsync();
    }

    public override async Task DisposeAsync()
    {
        try { if (Page?.Context is not null) await Page.Context.CloseAsync(); } catch { }
        try { if (Browser is not null) await Browser.CloseAsync(); } catch { }
        Playwright?.Dispose();
        await base.DisposeAsync();
    }
}
