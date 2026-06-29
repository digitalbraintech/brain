using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Xunit;

namespace DigitalBrain.Tests.E2E;

// Reproduces the desktop client transport: native gRPC (not gRPC-Web) over the proxied "grpc" endpoint,
// with WatchHomeFeed and the ExperienceStep Send on the SAME channel, at the real 3-replica replica count.
// Asserts the first ui-gallery hop is delivered back to the streaming client (cross-silo fanout included).
[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class NativeGrpcGalleryDeliveryE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task Gallery_start_hop_is_delivered_over_native_grpc()
    {
        E2EPrerequisites.RequireRenderE2E();

        using var channel = _fx.CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
        using var feed = client.WatchHomeFeed(new WatchHomeFeedRequest(), cancellationToken: cts.Token);

        var delivered = ReadForGalleryHopAsync(feed.ResponseStream, cts.Token);

        // Subscribe-before-emit: give the stream a moment to register, then fire start on the SAME channel.
        await Task.Delay(750, cts.Token);
        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "native-grpc-start",
            TypeName = nameof(ExperienceStep),
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["pack"] = "ui-gallery",
                ["experienceId"] = "ui-gallery",
                ["eventName"] = "start",
            })),
        }, cancellationToken: cts.Token);

        Assert.True(await delivered, "ui-gallery start hop was not delivered to the native-gRPC WatchHomeFeed stream");
    }

    static async Task<bool> ReadForGalleryHopAsync(IAsyncStreamReader<RfwCardEnvelope> stream, CancellationToken ct)
    {
        try
        {
            while (await stream.MoveNext(ct))
            {
                var json = stream.Current.DataJson;
                if (string.IsNullOrEmpty(json)) continue;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("activeExperience", out var marker) &&
                    marker.GetString() == "ui-gallery/ui-gallery")
                {
                    return true;
                }
            }
        }
        catch (RpcException) { }
        catch (OperationCanceledException) { }
        return false;
    }
}
