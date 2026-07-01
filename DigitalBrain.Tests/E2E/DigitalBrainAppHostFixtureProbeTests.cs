using System.Net;
using System.Net.Sockets;

namespace DigitalBrain.Tests.E2E;

public class DigitalBrainAppHostFixtureProbeTests
{
    [Fact]
    public async Task ProbeAsync_returns_true_when_something_is_listening()
    {
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        try
        {
            var acceptTask = listener.GetContextAsync();
            var probeTask = DigitalBrainAppHostFixture.ProbeAsync($"http://localhost:{port}/", TimeSpan.FromSeconds(2));

            var context = await acceptTask;
            context.Response.StatusCode = 200;
            context.Response.Close();

            Assert.True(await probeTask);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ProbeAsync_returns_false_when_nothing_is_listening()
    {
        var port = GetFreeTcpPort();

        var result = await DigitalBrainAppHostFixture.ProbeAsync($"http://localhost:{port}/", TimeSpan.FromMilliseconds(500));

        Assert.False(result);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
