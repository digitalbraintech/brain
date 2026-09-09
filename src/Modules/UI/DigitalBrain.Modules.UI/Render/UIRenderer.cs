using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Product.Identity;
using System.Security.Cryptography;
using System.Text;

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
        var surfaceGrain = GrainFactory.GetGrain<ISurface>(surface.ToGrainId());
        var receipt = await surfaceGrain
            .Open(signal.CommandId, new SurfaceScene(signal.SurfaceKey, signal.Title, signal.Root), RetainedScenes)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await RecordOutgoingAsync(new SurfaceOpened(signal.CommandId, Id, signal.SurfaceKey, signal.Title))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        foreach (var component in receipt.AddedComponents)
        {
            var eventId = StableComponentEventId(signal.CommandId, signal.SurfaceKey, component.Key!);
            var delivery = CreateDelivery(
                new ComponentAdded(signal.CommandId, Id, signal.SurfaceKey, component), signalId: eventId);
            await RecordOutgoingAsync(delivery)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    public async Task HandleAsync(ControlActivated signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        if (CurrentDelivery?.Principal is null)
        {
            throw new NeuronAuthorizationException("Control activation requires an authenticated principal.");
        }
        await RecordOutgoingAsync(signal)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

    private SignalId StableComponentEventId(CommandId commandId, string surfaceKey, string componentKey)
        => new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"component-added:{Id}:{commandId}:{surfaceKey}:{componentKey}")).AsSpan(0, 16)));
}
