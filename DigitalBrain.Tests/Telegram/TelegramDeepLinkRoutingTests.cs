using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Telegram.Channel;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Telegram;

// Verifies that GatewayService.Send routes a TelegramMessageReceived envelope
// to the per-chat TelegramChatNeuron rather than broadcasting via IngressNeuron.
[Collection("tg-routing-host")]
public class TelegramDeepLinkRoutingTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private HomeFeedBus _homeFeedBus = null!;

    public async Task InitializeAsync()
    {
        _homeFeedBus = new HomeFeedBus();
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    private GatewayService NewService() =>
        new(_cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    private static byte[] Json(long chatId, string text) =>
        System.Text.Encoding.UTF8.GetBytes(
            $"{{\"chatId\":{chatId},\"fromUserId\":1,\"text\":\"{text}\",\"updateId\":1}}");

    [Fact]
    public async Task Send_TelegramMessageReceived_start_routes_to_chat_neuron_and_binds()
    {
        await NewService().Send(new SynapseEnvelope
        {
            TypeName = "TelegramMessageReceived",
            CorrelationId = "tg-routing-1",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(Json(200, "/start hello-world"))
        }, TestServerCallContext.Create());

        var chat = _cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-200");
        Assert.Equal("hello-world", await chat.GetBoundBundleAsync());
    }

    [Fact]
    public async Task Send_TelegramMessageReceived_returns_the_same_envelope()
    {
        var envelope = new SynapseEnvelope
        {
            TypeName = "TelegramMessageReceived",
            CorrelationId = "tg-routing-2",
            Payload = global::Google.Protobuf.ByteString.CopyFrom(Json(201, "/start hello-world"))
        };

        var result = await NewService().Send(envelope, TestServerCallContext.Create());

        Assert.Equal(envelope.TypeName, result.TypeName);
        Assert.Equal(envelope.CorrelationId, result.CorrelationId);
    }
}

[CollectionDefinition("tg-routing-host", DisableParallelization = true)]
public sealed class TgRoutingHostCollection;
