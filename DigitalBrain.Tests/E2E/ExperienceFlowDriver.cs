using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

/// Reusable harness for browser E2Es of an experience: publish+install a domain pack, open its
/// full-screen host, then trigger/tap hops and assert each renders in Flutter. Generalizes the
/// boilerplate so a new experience E2E is ~10 lines. On a failed hop it dumps browser console
/// logs, page errors, and the DOM to the artifacts dir for diagnosis without re-deriving the
/// pipeline. For deeper debugging see the dart-MCP recipe in the class remarks below.
///
/// dart-MCP manual recipe (NOT wired here — the release web bundle has no Dart Tooling Daemon):
///   1) cd app && flutter run -d chrome --dart-define=DIGITALBRAIN_E2E=true   (debug, exposes DTD)
///   2) point it at the same kernel endpoint, drive the experience route
///   3) use the dart MCP tools get_widget_tree / get_runtime_errors against that running app.
public sealed class ExperienceFlowDriver
{
    readonly DigitalBrainBrowserFixture _fx;
    readonly string _pack;
    readonly string _experienceId;
    readonly List<string> _consoleLog = new();
    readonly string _artifactDir;

    public ExperienceFlowDriver(DigitalBrainBrowserFixture fixture, string pack, string experienceId)
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

    public async Task OpenAsync()
    {
        _fx.Page.Console += (_, msg) => _consoleLog.Add($"[{msg.Type}] {msg.Text}");
        _fx.Page.PageError += (_, err) => _consoleLog.Add($"[pageerror] {err}");

        // Web hash strategy: deep-link straight to the experience host. Wait for the WatchHomeFeed
        // response before any hop is emitted (HomeFeedBus has no replay; subscribe-before-emit).
        var url = _fx.GatewayHttpsUrl.TrimEnd('/') + $"/#/experience/{_pack}/{_experienceId}";
        await _fx.Page.RunAndWaitForResponseAsync(
            () => _fx.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load }),
            r => r.Url.Contains("WatchHomeFeed"),
            new() { Timeout = 60_000 });
    }

    public Task TriggerExperienceAsync(params (string, string)[] args) => StepAsync("start", args);

    public Task TapAsync(string eventName, params (string, string)[] args) => StepAsync(eventName, args);

    async Task StepAsync(string eventName, (string, string)[] args)
    {
        await _fx.SendExperienceStepAsync(_pack, _experienceId, eventName,
            args.ToDictionary(a => a.Item1, a => a.Item2));
    }

    public async Task AssertHopRendersAsync(string surfaceId)
    {
        var node = _fx.Page.Locator($"[flt-semantics-identifier=\"{surfaceId}\"]");
        try
        {
            await node.WaitForAsync(new() { Timeout = 30_000 });
            Assert.Equal(1, await node.CountAsync());
            await _fx.Page.ScreenshotAsync(new() { Path = Path.Combine(_artifactDir, $"e2e-{_pack}-{surfaceId}.png") });
        }
        catch
        {
            await DumpFailureAsync(surfaceId);
            throw;
        }
    }

    async Task DumpFailureAsync(string surfaceId)
    {
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(_artifactDir, $"console-{_pack}-{surfaceId}.log"), _consoleLog);
            var dom = await _fx.Page.ContentAsync();
            await File.WriteAllTextAsync(Path.Combine(_artifactDir, $"dom-{_pack}-{surfaceId}.html"), dom);
            await _fx.Page.ScreenshotAsync(new() { Path = Path.Combine(_artifactDir, $"FAILED-{_pack}-{surfaceId}.png") });
        }
        catch { /* diagnostics are best-effort; never mask the original assertion failure */ }
    }
}
