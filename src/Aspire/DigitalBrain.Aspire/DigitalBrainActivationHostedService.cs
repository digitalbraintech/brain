using DigitalBrain.Abstractions;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

internal sealed class DigitalBrainActivationHostedService(IDigitalBrain brain) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => brain.ActivateAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
