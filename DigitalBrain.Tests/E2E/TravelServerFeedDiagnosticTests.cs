using System.Text;
using DigitalBrain.Runtime.Grpc;
using Grpc.Core;
using Xunit;
using Xunit.Abstractions;

namespace DigitalBrain.Tests.E2E;

// Triage-only: isolates the server side of the travel slice. Subscribes to WatchHomeFeed via a native
// gRPC client (no browser), drives publish→install→step, and records every surface correlationId that
// the kernel broadcasts. If "travel-intro" never arrives here, the fault is server-side (publish/embody),
// not the Flutter render path.
[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class TravelServerFeedDiagnosticTests(DigitalBrainBrowserFixture fixture, ITestOutputHelper output)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;
    readonly ITestOutputHelper _out = output;

    [SkippableFact]
    public async Task ExperienceStep_start_broadcasts_travel_intro_on_homefeed()
    {
        E2EPrerequisites.RequireRenderE2E();

        await _fx.PublishPackAsync("travel", "1.0", code: TravelPackSource.Read(),
            description: "Travel domain — diagnostic");
        await _fx.InstallPackAsync("travel", "1.0", buyer: "e2e-diag");

        using var channel = _fx.CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var received = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var call = client.WatchHomeFeed(new WatchHomeFeedRequest(), cancellationToken: cts.Token);

        var reader = Task.Run(async () =>
        {
            try
            {
                while (await call.ResponseStream.MoveNext(cts.Token))
                {
                    var env = call.ResponseStream.Current;
                    lock (received) received.Add($"corr='{env.CorrelationId}' lib={env.LibraryName} root={env.RootWidget} dataLen={env.DataJson.Length}");
                    if (env.CorrelationId == "travel-intro") cts.CancelAfter(TimeSpan.FromSeconds(1));
                }
            }
            catch (RpcException) { }
            catch (OperationCanceledException) { }
        });

        await Task.Delay(1500); // let the subscription register before emitting (HomeFeedBus has no replay)
        await _fx.SendExperienceStepAsync("travel", "plan-trip", "start",
            new Dictionary<string, string> { ["prompt"] = "plan a trip to Bali next month" });

        try { await reader; } catch { }

        var report = new StringBuilder();
        lock (received)
        {
            report.AppendLine($"WatchHomeFeed surfaces received: {received.Count}");
            foreach (var r in received) report.AppendLine("  " + r);
        }
        _out.WriteLine(report.ToString());

        bool sawTravelIntro;
        lock (received) sawTravelIntro = received.Exists(r => r.Contains("corr='travel-intro'"));
        Assert.True(sawTravelIntro, "Server did not broadcast a 'travel-intro' surface. Received:\n" + report);
    }
}
