using DigitalBrain.Runtime.Grpc;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalBrain.Tests.Gateway;

[Collection("silo-host")]
public class GatewayGrpcWireTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly DigitalBrainGateway.DigitalBrainGatewayClient _client;

    public GatewayGrpcWireTests(WebApplicationFactory<Program> factory)
    {
        _channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler()
        });
        _client = new DigitalBrainGateway.DigitalBrainGatewayClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    [Fact]
    public async Task Health_OverGrpc_ReturnsOk()
    {
        var reply = await _client.HealthAsync(new HealthRequest());
        Assert.True(reply.Ok);
    }

    [Fact]
    public async Task Ask_Ino_OverGrpc_ReturnsText()
    {
        var reply = await _client.AskAsync(new AskRequest { NeuronId = "ino-main", Prompt = "hi" });
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }
}
