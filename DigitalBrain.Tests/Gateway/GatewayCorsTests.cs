using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalBrain.Tests.Gateway;

[Collection("silo-host")]
public class GatewayCorsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayCorsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Preflight_FromBrowserOrigin_AllowsOriginOnGrpcRoute()
    {
        var client = _factory.CreateClient();
        using var preflight = new HttpRequestMessage(
            HttpMethod.Options, "/digitalbrain.DigitalBrainGateway/Health");
        preflight.Headers.Add("Origin", "https://digitalbrain.tech");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-grpc-web");

        var response = await client.SendAsync(preflight);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://digitalbrain.tech", origins);
    }
}
