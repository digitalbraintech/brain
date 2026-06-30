using Telegram.BotAPI;

namespace DigitalBrain.Telegram.Transport;

// Holds the active ITelegramBotClient. The token may be absent at boot and
// arrive later via a ConfigurationProvided event, so the client is rebuilt
// atomically when the token changes. Consumers read Current per call rather
// than capturing it, so a late-arriving token takes effect immediately.
public sealed class TelegramBotAccessor(TelegramTransportOptions options)
{
    private ITelegramBotClient? _current = Build(options.BotToken, options.ApiServerAddress);

    public ITelegramBotClient? Current => _current;

    public bool HasToken => _current is not null;

    public void SetToken(string token)
    {
        _current = Build(token, options.ApiServerAddress);
    }

    private static ITelegramBotClient? Build(string token, string serverAddress)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return string.IsNullOrWhiteSpace(serverAddress)
            ? new TelegramBotClient(token)
            : new TelegramBotClient(new TelegramBotClientOptions(token) { ServerAddress = serverAddress });
    }
}
