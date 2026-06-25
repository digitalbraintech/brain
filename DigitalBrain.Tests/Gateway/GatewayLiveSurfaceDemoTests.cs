using System.Text.Json;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Net.Client;
using Xunit;

namespace DigitalBrain.Tests.Gateway;

public sealed class GatewayLiveSurfaceDemoTests
{
    [Fact]
    public async Task Live_SurfaceDemoRequested_StreamsActivityGraphAndPackSurface()
    {
        var endpoint = Environment.GetEnvironmentVariable("DIGITALBRAIN_LIVE_GATEWAY");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        using var channel = GrpcChannel.ForAddress(endpoint);
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var stream = client.WatchHomeFeed(new WatchHomeFeedRequest(), cancellationToken: timeout.Token);

        var correlationId = "live-smoke-" + Guid.NewGuid().ToString("N");
        var activityGraphSeen = false;
        var packSurfaceSeen = false;

        var reader = Task.Run(async () =>
        {
            while (await stream.ResponseStream.MoveNext(timeout.Token))
            {
                var current = stream.ResponseStream.Current;
                using var doc = JsonDocument.Parse(current.DataJson);
                var root = doc.RootElement;
                var kind = root.TryGetProperty("kind", out var kindElement)
                    ? kindElement.GetString()
                    : null;
                var surfaceId = root.TryGetProperty("surfaceId", out var surfaceElement)
                    ? surfaceElement.GetString()
                    : null;

                activityGraphSeen |= kind == "activity-graph"
                    || surfaceId == "surface.kernel.live-observability";
                packSurfaceSeen |= surfaceId == "surface-demo-pack"
                    || current.LibraryName.Contains("surface-demo-pack", StringComparison.OrdinalIgnoreCase);

                if (activityGraphSeen && packSurfaceSeen)
                {
                    break;
                }
            }
        }, timeout.Token);

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = correlationId,
            TypeName = "DigitalBrain.Kernel.SurfaceDemoRequested",
            Payload = ByteString.CopyFromUtf8("""{"source":"live-smoke"}""")
        }, cancellationToken: timeout.Token);

        await reader.WaitAsync(timeout.Token);

        Assert.True(activityGraphSeen);
        Assert.True(packSurfaceSeen);
    }
}
