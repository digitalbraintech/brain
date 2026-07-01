using DigitalBrain.Runtime.Grpc;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalBrain.Tests.Kernel;

[Collection("silo-host")]
public class KernelGrpcWebTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public KernelGrpcWebTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_Over_GrpcWeb_Succeeds()
    {
        var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, _factory.Server.CreateHandler());
        using var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = handler });
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var reply = await client.HealthAsync(new HealthRequest());
        Assert.True(reply.Ok);
    }
}
