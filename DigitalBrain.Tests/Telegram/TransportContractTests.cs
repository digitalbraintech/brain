extern alias TransportAssembly;

using System.Collections.Concurrent;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;
using Transport = TransportAssembly::DigitalBrain.Telegram.Transport;
// The gateway client + SynapseEnvelope used here MUST be the transport's generated
// copy (not the kernel's), so the forwarder/dispatcher type signatures line up.
using DigitalBrainGateway = TransportAssembly::DigitalBrain.Runtime.Grpc.DigitalBrainGateway;
using SynapseEnvelope = TransportAssembly::DigitalBrain.Runtime.Grpc.SynapseEnvelope;

namespace DigitalBrain.Tests.Telegram;

// Pins the Telegram API contract and the prop mapping so the transport is safe
// to ship without a manual smoke test against a live bot or live brain.
//   Test 1 (inbound):  a recorded webhook Update -> brain Send("TelegramMessageReceived", props).
//   Test 2 (outbound): a TelegramReplyRequested Signal -> a real sendMessage hitting a fake Telegram server.
public sealed class TransportContractTests
{
    // A realistic Telegram webhook Update for a plain text private-chat message,
    // captured from the Bot API wire format. The mapping under test reads
    // message.chat.id, message.from.id, message.text, and update_id.
    private const string RecordedUpdateJson = """
    {
      "update_id": 884213,
      "message": {
        "message_id": 57,
        "from": {
          "id": 1234567,
          "is_bot": false,
          "first_name": "Ada",
          "username": "ada_l",
          "language_code": "en"
        },
        "chat": {
          "id": 1234567,
          "first_name": "Ada",
          "username": "ada_l",
          "type": "private"
        },
        "date": 1718900000,
        "text": "hello brain"
      }
    }
    """;

    [Fact]
    public async Task Inbound_RecordedWebhookUpdate_CallsBrainSend_WithMappedProps()
    {
        var fakeGateway = new RecordingGatewayClient();
        var forwarder = new Transport.TelegramUpdateForwarder(
            fakeGateway, new Transport.TelegramTransportOptions(),
            NullLogger<Transport.TelegramUpdateForwarder>.Instance);

        var update = JsonSerializer.Deserialize<global::Telegram.BotAPI.GettingUpdates.Update>(RecordedUpdateJson)!;
        await forwarder.ForwardAsync(update);

        var sent = Assert.Single(fakeGateway.Sent);
        Assert.Equal("TelegramMessageReceived", sent.TypeName);

        var props = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(sent.Payload.Span)!;
        Assert.Equal(1234567L, props["chatId"].GetInt64());
        Assert.Equal(1234567L, props["fromUserId"].GetInt64());
        Assert.Equal("hello brain", props["text"].GetString());
        Assert.Equal(884213L, props["updateId"].GetInt64());
    }

    [Fact]
    public async Task Outbound_TelegramReplyRequested_HitsFakeTelegramServer_WithExactSendMessage()
    {
        await using var telegram = await FakeTelegramServer.StartAsync();

        var options = new Transport.TelegramTransportOptions
        {
            BotToken = "111:TEST",
            ApiServerAddress = telegram.BaseUrl,
        };
        var accessor = new Transport.TelegramBotAccessor(options);
        var webhookSetup = new Transport.TelegramWebhookSetup(
            accessor,
            new SingleClientHttpFactory(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<Transport.TelegramWebhookSetup>.Instance);
        var dispatcher = new Transport.TelegramReplyDispatcher(
            accessor, webhookSetup, new RecordingGatewayClient(), options,
            NullLogger<Transport.TelegramReplyDispatcher>.Instance);

        var reply = new SynapseEnvelope
        {
            TypeName = "TelegramReplyRequested",
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(new { chatId = 7, text = "yo" })),
        };
        await dispatcher.DispatchAsync(reply);

        var call = await telegram.WaitForMethodAsync("sendMessage", TimeSpan.FromSeconds(8));
        Assert.NotNull(call);
        Assert.Equal(7L, call!.Body!["chat_id"]!.GetValue<long>());
        Assert.Equal("yo", call.Body!["text"]!.GetValue<string>());
    }

    // Task 7b store-and-PULL: a PackConfigured notification for this transport's pack drives a point-to-point
    // GetPackConfig pull; the token comes from that RPC reply (never a broadcast) and is used to set the webhook.
    [Fact]
    public async Task PackConfigured_PullsTokenViaGetPackConfig_AndSetsWebhook()
    {
        await using var telegram = await FakeTelegramServer.StartAsync();

        var options = new Transport.TelegramTransportOptions
        {
            BotToken = string.Empty, // no token at boot — it must be pulled
            ApiServerAddress = telegram.BaseUrl,
            WebhookUrl = "https://example.test",
            PackName = "TelegramResponderNeuron",
            ConfigScope = "default",
            InternalServiceKey = "super-secret-internal-key",
        };
        var accessor = new Transport.TelegramBotAccessor(options);
        var webhookSetup = new Transport.TelegramWebhookSetup(
            accessor,
            new SingleClientHttpFactory(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<Transport.TelegramWebhookSetup>.Instance);
        var gateway = new ConfigPullGatewayClient(new Dictionary<string, string> { ["telegram_token"] = "123:ABC" });
        var dispatcher = new Transport.TelegramReplyDispatcher(
            accessor, webhookSetup, gateway, options, NullLogger<Transport.TelegramReplyDispatcher>.Instance);

        Assert.False(accessor.HasToken); // nothing to send with yet

        var notify = new SynapseEnvelope
        {
            TypeName = "PackConfigured",
            Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(
                new { pack = "TelegramResponderNeuron", scope = "default" })),
        };
        await dispatcher.DispatchAsync(notify);

        // The token was obtained via the GetPackConfig RPC (point-to-point), NOT from the broadcast payload.
        Assert.Equal(1, gateway.GetPackConfigCalls);
        // The transport presented the shared internal service key so the kernel admits the secret pull.
        Assert.Equal("super-secret-internal-key", gateway.LastInternalKeyHeader);
        Assert.True(accessor.HasToken);

        var call = await telegram.WaitForMethodAsync("setWebhook", TimeSpan.FromSeconds(8));
        Assert.NotNull(call);
        Assert.Contains("https://example.test/webhook", call!.Body!["url"]!.GetValue<string>());
    }

    private sealed class RecordingGatewayClient : DigitalBrainGateway.DigitalBrainGatewayClient
    {
        public List<SynapseEnvelope> Sent { get; } = new();

        public override AsyncUnaryCall<SynapseEnvelope> SendAsync(
            SynapseEnvelope request, CallOptions options)
        {
            Sent.Add(request);
            return new AsyncUnaryCall<SynapseEnvelope>(
                Task.FromResult(request),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }
    }

    // Fake gateway that returns config only via the GetPackConfig RPC — so a test asserting the token was
    // obtained here (and not from a broadcast) is meaningful.
    private sealed class ConfigPullGatewayClient(IReadOnlyDictionary<string, string> values)
        : DigitalBrainGateway.DigitalBrainGatewayClient
    {
        public int GetPackConfigCalls { get; private set; }
        public string? LastInternalKeyHeader { get; private set; }

        public override AsyncUnaryCall<TransportAssembly::DigitalBrain.Runtime.Grpc.PackConfigReply> GetPackConfigAsync(
            TransportAssembly::DigitalBrain.Runtime.Grpc.GetPackConfigRequest request, CallOptions options)
        {
            GetPackConfigCalls++;
            LastInternalKeyHeader = options.Headers?.GetValue("x-internal-key");
            var reply = new TransportAssembly::DigitalBrain.Runtime.Grpc.PackConfigReply();
            foreach (var (k, v) in values)
                reply.Values[k] = v;
            return new AsyncUnaryCall<TransportAssembly::DigitalBrain.Runtime.Grpc.PackConfigReply>(
                Task.FromResult(reply),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }
    }

    private sealed class SingleClientHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

// In-process stand-in for api.telegram.org. ITelegramBotClient is pointed here
// via TelegramTransportOptions.ApiServerAddress so real sendMessage requests are
// captured and asserted against, pinning the wire contract.
internal sealed record CapturedTelegramCall(string Method, System.Text.Json.Nodes.JsonNode? Body);

internal sealed class FakeTelegramServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<CapturedTelegramCall> _calls = new();
    private readonly System.Threading.Channels.Channel<CapturedTelegramCall> _callChannel =
        System.Threading.Channels.Channel.CreateUnbounded<CapturedTelegramCall>();

    public string BaseUrl { get; private set; } = string.Empty;

    private FakeTelegramServer(WebApplication app) => _app = app;

    public static async Task<FakeTelegramServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var server = new FakeTelegramServer(app);

        app.MapPost("/bot{token}/{method}", async (string method, HttpContext ctx) =>
        {
            System.Text.Json.Nodes.JsonNode? body = null;
            try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(ctx.Request.Body); }
            catch { body = null; }

            server.Record(new CapturedTelegramCall(method, body));

            return Results.Json(new
            {
                ok = true,
                result = new
                {
                    message_id = 1,
                    chat = new { id = body?["chat_id"]?.GetValue<long>() ?? 0L },
                    text = body?["text"]?.GetValue<string>() ?? string.Empty,
                },
            });
        });

        app.MapGet("/bot{token}/{method}", (string method) =>
        {
            server.Record(new CapturedTelegramCall(method, null));
            return Results.Json(new { ok = true, result = true });
        });

        await app.StartAsync();
        server.BaseUrl = app.Urls.First();
        return server;
    }

    private void Record(CapturedTelegramCall call)
    {
        _calls.Enqueue(call);
        _callChannel.Writer.TryWrite(call);
    }

    public async Task<CapturedTelegramCall?> WaitForMethodAsync(string method, TimeSpan timeout, CancellationToken ct = default)
    {
        var match = _calls.FirstOrDefault(c => string.Equals(c.Method, method, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await foreach (var call in _callChannel.Reader.ReadAllAsync(timeoutCts.Token))
            {
                if (string.Equals(call.Method, method, StringComparison.OrdinalIgnoreCase))
                    return call;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return null;
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _callChannel.Writer.TryComplete();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
