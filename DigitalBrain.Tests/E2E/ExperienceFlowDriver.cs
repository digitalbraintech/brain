using Microsoft.Playwright;

namespace DigitalBrain.Tests.E2E;

/// <summary>
/// Verifies that a live Flutter client (running in real browser via the full stack)
/// correctly rendered a server-driven surface. 
///
/// Philosophy: Interactions are driven by sending the exact <see cref="ExperienceStep"/> synapses
/// that a real button/form in the UI would produce. This is the correct abstraction for a
/// neuron/synapse system. The browser is used only to confirm that the declarative tree
/// was turned into accessible, visible widgets (via Flutter Semantics) and looks correct.
///
/// This class is intentionally thin — focused on "live render verification".
/// For fast, type-safe, in-memory testing of experiences and trees, use the UiTesting
/// harnesses (see DigitalBrain.Tests.Ui and KitExperienceTests).
///
/// For even faster introspection when running a debug flutter app, use the dart MCP tools
/// (get_widget_tree etc.) as documented in the original driver comments.
/// </summary>
public sealed class LiveRenderVerifier
{
    private readonly DigitalBrainBrowserFixture _fx;
    private readonly string _pack;
    private readonly string _experienceId;
    private readonly List<string> _consoleLog = new();
    private readonly string _artifactDir;

    public LiveRenderVerifier(DigitalBrainBrowserFixture fixture, string pack, string experienceId)
    {
        _fx = fixture;
        _pack = pack;
        _experienceId = experienceId;
        _artifactDir = Path.Combine(AppContext.BaseDirectory, "e2e-screenshots");
        Directory.CreateDirectory(_artifactDir);
    }

    public async Task PublishAndInstallAsync(string code, string description,
        string version = "1.0", string buyer = "e2e", double commissionRate = 0.0)
    {
        await _fx.PublishPackAsync(_pack, version, code: code, commissionRate: commissionRate, description: description);
        await _fx.InstallPackAsync(_pack, version, buyer: buyer);
    }

    /// <summary>
    /// Navigates the live Flutter app to the experience route so the real renderer
    /// (RFW + UiSurfaceTreeRenderer + ui_kit) processes the surfaces emitted by the backend.
    /// </summary>
    public async Task OpenAsync()
    {
        _fx.Page.Console += (_, msg) => _consoleLog.Add($"[{msg.Type}] {msg.Text}");
        _fx.Page.PageError += (_, err) => _consoleLog.Add($"[pageerror] {err}");

        var url = _fx.GatewayHttpsUrl.TrimEnd('/') + $"/#/experience/{_pack}/{_experienceId}";
        await _fx.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load });

        // Wait for Flutter to render something with semantics (the stable contract we use for asserts).
        // This makes navigation robust even if the initial load state is reached before the app hydrates.
        await _fx.Page.WaitForSelectorAsync("[flt-semantics]", new() { Timeout = 30000, State = WaitForSelectorState.Attached });
    }

    /// <summary>
    /// Simulates the user action (tap / submit) by sending the synapse the UI would send.
    /// This is the recommended way — deterministic and aligned with the architecture.
    /// </summary>
    public Task SendUserActionAsync(string action, params (string key, string value)[] formValues)
        => _fx.SendExperienceStepAsync(_pack, _experienceId, action,
            formValues.ToDictionary(x => x.key, x => x.value));

    /// <summary>
    /// Asserts that the live Flutter render produced a node for the given surface/hop id
    /// using Flutter Semantics (the stable, accessibility-first contract between server
    /// trees and client rendering).
    /// </summary>
    public async Task AssertSurfaceRenderedAsync(string surfaceId)
    {
        var timeoutMs = string.Equals(Environment.GetEnvironmentVariable("FAST_UI_E2E"), "1", StringComparison.OrdinalIgnoreCase)
            ? 8_000
            : 30_000;

        var node = _fx.Page.Locator($"[flt-semantics-identifier=\"{surfaceId}\"]");
        try
        {
            await node.WaitForAsync(new() { Timeout = timeoutMs });
            Assert.Equal(1, await node.CountAsync());
            await _fx.Page.ScreenshotAsync(new() { Path = Path.Combine(_artifactDir, $"e2e-{_pack}-{surfaceId}.png") });
        }
        catch
        {
            await DumpDiagnosticAsync(surfaceId);
            throw;
        }
    }

    private async Task DumpDiagnosticAsync(string surfaceId)
    {
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(_artifactDir, $"console-{_pack}-{surfaceId}.log"), _consoleLog);
            var dom = await _fx.Page.ContentAsync();
            await File.WriteAllTextAsync(Path.Combine(_artifactDir, $"dom-{_pack}-{surfaceId}.html"), dom);
            await _fx.Page.ScreenshotAsync(new() { Path = Path.Combine(_artifactDir, $"FAILED-{_pack}-{surfaceId}.png") });
        }
        catch { /* best effort */ }
    }
}

// Back-compat alias during transition. New code should prefer LiveRenderVerifier.
[Obsolete("Use LiveRenderVerifier for clarity. This alias will be removed.")]
public sealed class ExperienceFlowDriver
{
    private readonly LiveRenderVerifier _inner;

    public ExperienceFlowDriver(DigitalBrainBrowserFixture fixture, string pack, string experienceId)
    {
        _inner = new LiveRenderVerifier(fixture, pack, experienceId);
    }

    public Task PublishAndInstallAsync(string code, string description, string version = "1.0", string buyer = "e2e", double commissionRate = 0.0)
        => _inner.PublishAndInstallAsync(code, description, version, buyer, commissionRate);

    public Task OpenAsync() => _inner.OpenAsync();
    public Task SendUserActionAsync(string action, params (string key, string value)[] formValues)
        => _inner.SendUserActionAsync(action, formValues);
    public Task AssertSurfaceRenderedAsync(string surfaceId) => _inner.AssertSurfaceRenderedAsync(surfaceId);

    // Old API shims (for gradual migration of existing tests)
    public Task TriggerExperienceAsync(params (string, string)[] args) => SendUserActionAsync("start", args);
    public Task TapAsync(string eventName, params (string, string)[] args) => SendUserActionAsync(eventName, args);
    public Task AssertHopRendersAsync(string surfaceId) => AssertSurfaceRenderedAsync(surfaceId);
}
