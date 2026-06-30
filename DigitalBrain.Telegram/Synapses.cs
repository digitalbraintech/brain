namespace DigitalBrain.Telegram;

// Transport-internal typed records for the Telegram integration. The kernel and pack layer
// work with Signal (name+props); only the Telegram transport adapter uses these concrete types.
// They are NOT journaled through the kernel — they do NOT belong in JournalJsonContext.

[GenerateSerializer]
public record TelegramMessageReceived(
    [property: Id(0)] long ChatId,
    [property: Id(1)] long FromUserId,
    [property: Id(2)] string Text,
    [property: Id(3)] long UpdateId)
    : DigitalBrain.Core.Synapse(nameof(TelegramMessageReceived), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record TelegramReplyRequested(
    [property: Id(0)] long ChatId,
    [property: Id(1)] string Text,
    [property: Id(2)] long? ReplyToMessageId = null)
    : DigitalBrain.Core.Synapse(nameof(TelegramReplyRequested), DateTimeOffset.UtcNow);
