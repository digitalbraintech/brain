using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Google;

internal sealed class GmailMaintenanceWorker(GmailDraftPreviews previews) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            previews.Prune();
        }
    }
}
