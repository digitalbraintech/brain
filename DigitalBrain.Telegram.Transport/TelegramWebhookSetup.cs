using System.Text.Json;
using Microsoft.Extensions.Options;
using Telegram.BotAPI.GettingUpdates;

namespace DigitalBrain.Telegram.Transport;

// Resolves the public HTTPS origin (from Telegram:WebhookUrl or by polling the
// local ngrok admin API), then registers the /webhook callback with Telegram.
// No-ops cleanly when there is no token (the host stays up) or when neither a
// configured URL nor an ngrok tunnel yields a public origin.
public sealed class TelegramWebhookSetup(
    TelegramBotAccessor botAccessor,
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramTransportOptions> options,
    ILogger<TelegramWebhookSetup> logger)
{
    public async Task RegisterAsync(CancellationToken ct = default)
    {
        var config = options.Value;
        var bot = botAccessor.Current;
        if (bot is null)
        {
            logger.LogInformation(
                "Telegram bot token not configured — skipping webhook setup. The transport will register once a token arrives.");
            return;
        }

        var publicUrl = config.WebhookUrl;
        if (string.IsNullOrWhiteSpace(publicUrl) && !string.IsNullOrWhiteSpace(config.NgrokApiUrl))
            publicUrl = await ResolveNgrokUrlAsync(config.NgrokApiUrl, ct);

        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            logger.LogWarning("No webhook URL configured and ngrok not available — Telegram updates won't be delivered.");
            return;
        }

        var webhookUrl = publicUrl.TrimEnd('/') + "/webhook";
        try
        {
            await bot.SetWebhookAsync(
                webhookUrl,
                secretToken: string.IsNullOrWhiteSpace(config.WebhookSecretToken) ? null : config.WebhookSecretToken,
                cancellationToken: ct);
            logger.LogInformation("Webhook registered: {Url}", webhookUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set webhook at {Url}", webhookUrl);
        }
    }

    private async Task<string?> ResolveNgrokUrlAsync(string ngrokApiUrl, CancellationToken ct)
    {
        // Ngrok may take a few seconds to come up — retry with linear backoff.
        // The admin API serves /api/tunnels; the bot needs the first HTTPS one.
        using var http = httpClientFactory.CreateClient();
        var apiBase = ngrokApiUrl.TrimEnd('/');

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2 + attempt), ct);
                var json = await http.GetStringAsync($"{apiBase}/api/tunnels", ct);
                using var doc = JsonDocument.Parse(json);

                foreach (var tunnel in doc.RootElement.GetProperty("tunnels").EnumerateArray())
                {
                    var publicUrl = tunnel.GetProperty("public_url").GetString();
                    if (publicUrl is not null && publicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("Resolved ngrok public URL: {Url}", publicUrl);
                        return publicUrl;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Waiting for ngrok tunnel (attempt {Attempt}): {Message}", attempt + 1, ex.Message);
            }
        }

        logger.LogWarning("Could not resolve ngrok tunnel URL after retries.");
        return null;
    }
}
