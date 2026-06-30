using DigitalBrain.Runtime.Grpc;
using Grpc.Core;

namespace DigitalBrain.Telegram.Transport;

// Long-running consumer: registers the webhook at boot, then opens a filtered
// WatchSynapses stream and feeds each Signal to the reply dispatcher. Reconnects
// with backoff if the stream drops (the brain replicas roll independently).
public sealed class SynapseStreamConsumer(
    DigitalBrainGateway.DigitalBrainGatewayClient gateway,
    TelegramReplyDispatcher dispatcher,
    TelegramWebhookSetup webhookSetup,
    ILogger<SynapseStreamConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await webhookSetup.RegisterAsync(stoppingToken);

        // Pull once at startup in case config already exists (we may have booted after the form was submitted).
        // This sets the token + webhook point-to-point without waiting for a PackConfigured notification.
        await dispatcher.PullConfigAndApplyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WatchSynapses stream dropped — reconnecting in 3s.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var request = new WatchSynapsesRequest();
        request.TypeFilter.AddRange(dispatcher.WatchedTypes);

        using var call = gateway.WatchSynapses(request, cancellationToken: ct);
        await foreach (var envelope in call.ResponseStream.ReadAllAsync(ct))
        {
            await dispatcher.DispatchAsync(envelope, ct);
        }
    }
}
