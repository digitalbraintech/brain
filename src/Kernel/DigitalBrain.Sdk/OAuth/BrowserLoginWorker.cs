using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Sdk;

// Login completion belongs to the kernel, not to the browser connection's lifetime: the durable
// turn resumes even when the user closes the tab before the callback response finishes.
public sealed class BrowserLoginWorker<TLogins>(TLogins logins, ILogger<BrowserLoginWorker<TLogins>> logger) : BackgroundService
    where TLogins : BrowserLogins
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await logins.DeliverAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Do not attach the exception: remote exception payloads can contain credentials.
                logger.LogWarning("A {Provider} login completion could not be delivered; the kernel will retry it.", logins.Definition.DisplayName);
            }
        }
    }
}
