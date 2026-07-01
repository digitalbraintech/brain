using Microsoft.Playwright;

namespace DigitalBrain.Tests.E2E;

// Regression guard for the gRPC-Web action-dispatch fix: the browser uses gRPC-Web (no
// client/bidi streaming), so kit/form actions must travel over the UNARY Send RPC, not the
// bidirectional EngageUiSession. Before the fix, clicking Sign In sent the LoginRequest over
// EngageUiSession, which gRPC-Web silently cannot carry, so nothing happened. This drives the
// real login form in a real browser and asserts the signed-in shell renders.
[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class LoginRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task Signing_in_dispatches_over_unary_send_and_renders_the_shell()
    {
        E2EPrerequisites.RequireRenderE2E();

        // The shell at "/" shows the login surface (sent when WatchHomeFeed opens). Wait for the
        // feed response so the stream is live before we interact (HomeFeedBus has no replay).
        var shellUrl = _fx.GatewayHttpsUrl.TrimEnd('/') + "/#/";
        await _fx.Page.RunAndWaitForResponseAsync(
            () => _fx.Page.GotoAsync(shellUrl, new() { WaitUntil = WaitUntilState.Load }),
            r => r.Url.Contains("WatchHomeFeed"),
            new() { Timeout = 60_000 });

        var shotDir = System.IO.Path.Combine(AppContext.BaseDirectory, "e2e-screenshots");
        System.IO.Directory.CreateDirectory(shotDir);

        // First-user provisioning: on fresh state any credentials bootstrap the admin account.
        var username = _fx.Page.Locator("[flt-semantics-identifier=\"field-username\"]");
        await username.WaitForAsync(new() { Timeout = 60_000 });
        await _fx.Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(shotDir, "e2e-login-form.png") });

        await username.ClickAsync();
        await _fx.Page.Keyboard.TypeAsync("e2e-admin");

        var password = _fx.Page.Locator("[flt-semantics-identifier=\"field-password\"]");
        await password.ClickAsync();
        await _fx.Page.Keyboard.TypeAsync("e2e-password");

        await _fx.Page.Locator("[flt-semantics-identifier=\"form-submit\"]").ClickAsync();

        // The signed-in shell tree only arrives over WatchHomeFeed AFTER the kernel handles the
        // LoginRequest — i.e. only if the unary Send dispatch reached the server.
        var shell = _fx.Page.Locator("[flt-semantics-identifier=\"app-shell-ready\"]");
        await shell.WaitForAsync(new() { Timeout = 30_000 });
        Assert.Equal(1, await shell.CountAsync());
        await _fx.Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(shotDir, "e2e-login-signed-in.png") });
    }
}
