extern alias GatewayAssembly;

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using GatewayProgram = GatewayAssembly::Program;

namespace DigitalBrain.Tests.Gateway;

public class GatewayEndpointsTests : IClassFixture<WebApplicationFactory<GatewayProgram>>
{
    private readonly WebApplicationFactory<GatewayProgram> _factory;
    public GatewayEndpointsTests(WebApplicationFactory<GatewayProgram> factory) => _factory = factory;

    [Fact]
    public async Task Health_Returns200()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Status_ReturnsJsonWithLlmMode()
    {
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/status");
        Assert.Contains("llmMode", body);
    }
}
