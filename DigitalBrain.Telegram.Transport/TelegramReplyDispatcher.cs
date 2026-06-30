using System.Text.Json;
using DigitalBrain.Runtime.Grpc;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;

namespace DigitalBrain.Telegram.Transport;

// Outbound half of the transport: turns broadcast Signals streamed from the
// brain's WatchSynapses into Telegram API calls.
//   TelegramReplyRequested  -> sendMessage(chatId, text)
//   ConfigurationProvided   -> adopt the token + (re)register the webhook
// Each Signal arrives as a SynapseEnvelope whose Payload is UTF-8 JSON of the
// Signal props.
public sealed class TelegramReplyDispatcher(
    TelegramBotAccessor botAccessor,
    TelegramWebhookSetup webhookSetup,
    TelegramTransportOptions options,
    ILogger<TelegramReplyDispatcher> logger)
{
    public const string ReplyRequestedType = "TelegramReplyRequested";
    public const string ConfigurationProvidedType = "ConfigurationProvided";

    public IReadOnlyList<string> WatchedTypes => new[] { ReplyRequestedType, ConfigurationProvidedType };

    public async Task DispatchAsync(SynapseEnvelope envelope, CancellationToken ct = default)
    {
        var props = ReadProps(envelope);

        switch (envelope.TypeName)
        {
            case ReplyRequestedType:
                await SendReplyAsync(props, ct);
                break;
            case ConfigurationProvidedType:
                await ApplyConfigurationAsync(props, ct);
                break;
            default:
                logger.LogDebug("Ignoring unwatched synapse {TypeName}", envelope.TypeName);
                break;
        }
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

    private async Task ApplyConfigurationAsync(IReadOnlyDictionary<string, JsonElement> props, CancellationToken ct)
    {
        var pack = props.TryGetValue("pack", out var packEl) ? packEl.GetString()
                 : props.TryGetValue("packName", out var nameEl) ? nameEl.GetString()
                 : null;
        if (!string.IsNullOrEmpty(pack) && !string.Equals(pack, options.PackName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("ConfigurationProvided for pack {Pack} is not for this transport ({Own})", pack, options.PackName);
            return;
        }

        var token = props.TryGetValue("telegram_token", out var tokenEl) ? tokenEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(token))
            return;

        botAccessor.SetToken(token);
        logger.LogInformation("Adopted Telegram token from ConfigurationProvided; re-registering webhook.");
        await webhookSetup.RegisterAsync(ct);
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadProps(SynapseEnvelope envelope)
    {
        if (envelope.Payload.IsEmpty)
            return new Dictionary<string, JsonElement>();

        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(envelope.Payload.Span);
        return parsed ?? new Dictionary<string, JsonElement>();
    }
}
