using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Telegram.BotAPI.GettingUpdates;

namespace DigitalBrain.Telegram.Transport;

// Inbound half of the transport: maps a Telegram webhook Update to a generic
// brain Send. A text message becomes Send("TelegramMessageReceived", {chatId,
// fromUserId, text, updateId}); the brain's reactive loop takes it from there.
// Non-text / non-message updates are dropped — voice/media are out of scope.
public sealed class TelegramUpdateForwarder(
    DigitalBrainGateway.DigitalBrainGatewayClient gateway,
    TelegramTransportOptions options,
    ILogger<TelegramUpdateForwarder> logger)
{
    public const string MessageReceivedType = "TelegramMessageReceived";

    // Must match GatewayService.InternalKeyHeader on the kernel side. The inbound generic Send is internal-only,
    // so the transport presents the shared service-to-service key the same way the reply dispatcher does for the
    // secret pull. Empty in local "clone + run" dev, where the kernel admits the call unauthenticated in Development.
    private const string InternalKeyHeader = "x-internal-key";

    public async Task ForwardAsync(Update update, CancellationToken ct = default)
    {
        var message = update.Message;
        var text = message?.Text;
        if (message is null || message.From is null || string.IsNullOrEmpty(text))
            return;

        var props = new Dictionary<string, object?>
        {
            ["chatId"] = message.Chat.Id,
            ["fromUserId"] = message.From.Id,
            ["text"] = text,
            ["updateId"] = update.UpdateId,
        };

        var envelope = new SynapseEnvelope
        {
            TypeName = MessageReceivedType,
            CorrelationId = $"tg:{message.Chat.Id}",
            Payload = ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(props)),
        };

        var headers = new Grpc.Core.Metadata();
        if (!string.IsNullOrEmpty(options.InternalServiceKey))
            headers.Add(InternalKeyHeader, options.InternalServiceKey);

        try
        {
            await gateway.SendAsync(envelope, headers: headers, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to forward Telegram update {UpdateId} to the brain", update.UpdateId);
        }
    }
}
