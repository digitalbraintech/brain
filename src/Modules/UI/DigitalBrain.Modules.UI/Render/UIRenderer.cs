using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.UI;

[GrainType("uirenderer")]
internal sealed class UIRenderer(NeuronRuntime runtime) : Neuron(runtime), IUIRenderer
{
    private const int RetainedScenes = 64;

    public async Task HandleAsync(OpenSurface signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(signal.SurfaceKey)
            || string.IsNullOrWhiteSpace(signal.Title))
        {
            return;
        }

        var surface = EntityId.For<ISurface>(Id.Owner, Id.Name);
        await GrainFactory
            .GetGrain<ISurface>(surface.ToGrainId())
            .Open(new SurfaceScene(signal.SurfaceKey, signal.Title, signal.Root), RetainedScenes)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await RecordOutgoingAsync(new SurfaceOpened(signal.CommandId, Id, signal.SurfaceKey, signal.Title))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(ControlActivated signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task HandleAsync(ActivityChanged signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (signal.Activity.Principal != CurrentDelivery?.Principal)
        {
            throw new NeuronAuthorizationException("Surface activity updates must retain their verified principal.");
        }
        var surface = EntityId.For<ISurface>(Id.Owner, Id.Name);
        await GrainFactory.GetGrain<ISurface>(surface.ToGrainId()).ApplyActivity(signal.Activity, 100)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await RecordOutgoingAsync(signal).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
