using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class TravelPlanTripRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task PlanTrip_walks_hops_and_each_renders_in_flutter()
    {
        E2EPrerequisites.RequireRenderE2E();

        await _fx.PublishPackAsync("travel", "1.0", code: TravelPackSource.Read(),
            description: "Travel domain — Plan a trip experience");
        await _fx.InstallPackAsync("travel", "1.0", buyer: "e2e-travel");

        // Live pack surfaces render on the canvas, where each WatchHomeFeed card becomes a panel
        // whose semantics identifier is the card's correlationId (== the per-hop surfaceId). The
        // neuron-driven shell at "/" only renders shell/widget-tree surfaces, so it ignores these.
        // Web uses hash URL strategy (see app/lib/main.dart), hence the "/#/canvas" deep link.
        var canvasUrl = _fx.GatewayHttpsUrl.TrimEnd('/') + "/#/canvas";

        // HomeFeedBus does not replay to late subscribers and dedups identical re-sends, so the
        // canvas WatchHomeFeed stream MUST be open before the first hop is emitted. Wait for the
        // stream's response to arrive (triggered while the canvas mounts) before stepping.
        await _fx.Page.RunAndWaitForResponseAsync(
            () => _fx.Page.GotoAsync(canvasUrl, new() { WaitUntil = WaitUntilState.Load }),
            r => r.Url.Contains("WatchHomeFeed"),
            new() { Timeout = 60_000 });

        // Screenshots are collectable test artifacts: anchor under the test output dir, not the user temp.
        var shotDir = System.IO.Path.Combine(AppContext.BaseDirectory, "e2e-screenshots");
        System.IO.Directory.CreateDirectory(shotDir);

        await Step("start",            "travel-intro",      ("prompt", "plan a trip to Bali next month"));
        await Step("flight.selected",  "travel-hotels",     ("flightId", "FL-001"));
        await Step("hotel.selected",   "travel-events",     ("hotelId", "H-001"));
        await Step("event.selected",   "travel-activities", ("eventId", "EV-001"));
        await Step("activity.selected","travel-summary",    ("activityId", "AC-001"));

        async Task Step(string eventName, string surfaceId, params (string, string)[] args)
        {
            await _fx.SendExperienceStepAsync("travel", "plan-trip", eventName,
                args.ToDictionary(a => a.Item1, a => a.Item2));
            var node = _fx.Page.Locator($"[flt-semantics-identifier=\"{surfaceId}\"]");
            await node.WaitForAsync(new() { Timeout = 30_000 });
            Assert.Equal(1, await node.CountAsync());
            await _fx.Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(shotDir, $"e2e-travel-{surfaceId}.png") });
        }
    }
}
