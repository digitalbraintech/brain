using System.Text.Json;
using DigitalBrain.Runtime.Grpc;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;

namespace DigitalBrain.Telegram.Transport;

// Outbound half of the transport: turns broadcast Signals streamed from the
// brain's WatchSynapses into Telegram API calls.
//   TelegramReplyRequested -> sendMessage(chatId, text)
//   PackConfigured         -> point-to-point GetPackConfig pull of the token, then (re)register the webhook
// The token is NEVER carried on the broadcast — PackConfigured only names the pack/scope that changed, and the
// transport pulls the secret over the internal gRPC channel. Each Signal arrives as a SynapseEnvelope whose
// Payload is UTF-8 JSON of the Signal props.
public sealed class TelegramReplyDispatcher(
    TelegramBotAccessor botAccessor,
    TelegramWebhookSetup webhookSetup,
    DigitalBrainGateway.DigitalBrainGatewayClient gateway,
    TelegramTransportOptions options,
    ILogger<TelegramReplyDispatcher> logger)
{
    public const string ReplyRequestedType = "TelegramReplyRequested";
    public const string PackConfiguredType = "PackConfigured";

    public IReadOnlyList<string> WatchedTypes => new[] { ReplyRequestedType, PackConfiguredType };

    public async Task DispatchAsync(SynapseEnvelope envelope, CancellationToken ct = default)
    {
        var props = ReadProps(envelope);

        switch (envelope.TypeName)
        {
            case ReplyRequestedType:
                await SendReplyAsync(props, ct);
                break;
            case PackConfiguredType:
                await OnPackConfiguredAsync(props, ct);
                break;
            default:
                logger.LogDebug("Ignoring unwatched synapse {TypeName}", envelope.TypeName);
                break;
        }
    }

    // Pull the token for this transport's pack/scope and adopt it. Used both on a PackConfigured notification
    // and once at startup (config may already exist). No-op when no token is stored yet.
    public async Task PullConfigAndApplyAsync(CancellationToken ct = default)
    {
        PackConfigReply reply;
        try
        {
            reply = await gateway.GetPackConfigAsync(
                new GetPackConfigRequest { Scope = options.ConfigScope, Pack = options.PackName },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GetPackConfig pull failed for pack {Pack}", options.PackName);
            return;
        }

        if (!reply.Values.TryGetValue("telegram_token", out var token) || string.IsNullOrWhiteSpace(token))
            return;

        botAccessor.SetToken(token);
        logger.LogInformation("Adopted Telegram token pulled for pack {Pack}; re-registering webhook.", options.PackName);
        await webhookSetup.RegisterAsync(ct);
    }

    private async Task SendReplyAsync(IReadOnlyDictionary<string, JsonElement> props, CancellationToken ct)
    {
        var bot = botAccessor.Current;
        if (bot is null)
        {
            logger.LogWarning("Reply requested but no Telegram token is configured — dropping message.");
            return;
        }

        if (!props.TryGetValue("chatId", out var chatEl) || !chatEl.TryGetInt64(out var chatId))
        {
            logger.LogWarning("TelegramReplyRequested missing a numeric chatId — dropping message.");
            return;
        }

        var text = props.TryGetValue("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            await bot.SendMessageAsync(chatId, text, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Telegram reply to chat {ChatId}", chatId);
        }
    }

    private async Task OnPackConfiguredAsync(IReadOnlyDictionary<string, JsonElement> props, CancellationToken ct)
    {
        var pack = props.TryGetValue("pack", out var packEl) ? packEl.GetString()
                 : props.TryGetValue("packName", out var nameEl) ? nameEl.GetString()
                 : null;
        if (!string.IsNullOrEmpty(pack) && !string.Equals(pack, options.PackName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("PackConfigured for pack {Pack} is not for this transport ({Own})", pack, options.PackName);
            return;
        }

        await PullConfigAndApplyAsync(ct);
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadProps(SynapseEnvelope envelope)
    {
        if (envelope.Payload.IsEmpty)
            return new Dictionary<string, JsonElement>();

        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(envelope.Payload.Span);
        return parsed ?? new Dictionary<string, JsonElement>();
    }
}
