namespace DigitalBrain.Telegram.Transport;

// Bound from the "Telegram" configuration section. Every field is optional: when
// BotToken is empty the host still boots and serves /webhook, but webhook
// registration and outbound sends are skipped until a token arrives (via config
// or a ConfigurationProvided event over WatchSynapses). This preserves the
// "clone + run with no secrets" path.
//
// Webhook resolution order: WebhookUrl if set, otherwise poll NgrokApiUrl's
// /api/tunnels for the first HTTPS tunnel, otherwise no webhook is registered.
public sealed class TelegramTransportOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public string NgrokApiUrl { get; set; } = string.Empty;

    // Override of the Telegram Bot API origin. Empty -> https://api.telegram.org.
    // Tests point this at an in-process fake server to pin the wire contract.
    public string ApiServerAddress { get; set; } = string.Empty;

    // The pack whose ConfigurationProvided events carry this transport's token.
    public string PackName { get; set; } = "TelegramResponderNeuron";
}
