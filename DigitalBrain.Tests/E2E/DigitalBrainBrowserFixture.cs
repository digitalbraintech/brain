using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

/// Extends the AppHost fixture with a real Chromium (Playwright) page.
/// Local runs are headed so you can watch the Flutter UI render live when packs embody and stream RfwCards.
/// CI (CI=true) forces headless.
public class DigitalBrainBrowserFixture : DigitalBrainAppHostFixture
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        if (App is null) return; // base skipped boot (prereqs absent)

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

        // Navigation is performed by the test when ready (after pack install etc.).
        // This keeps fixture init fast and robust even if the web UI resource isn't serving pages yet.
    }

    public override async Task DisposeAsync()
    {
        try { if (Page?.Context is not null) await Page.Context.CloseAsync(); } catch { }
        try { if (Browser is not null) await Browser.CloseAsync(); } catch { }
        Playwright?.Dispose();
        await base.DisposeAsync();
    }
}
