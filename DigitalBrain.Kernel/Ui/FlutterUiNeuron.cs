using DigitalBrain.Core;
using DigitalBrain.UiKit;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.flutter-ui.v1")]
public sealed class FlutterUiNeuron(ILogger<FlutterUiNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IFlutterUiNeuron
{
    public async Task HandleAsync(UiSurface surface)
    {
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is not null)
        {
            var card = UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value);
            bus.Broadcast(card);
        }

        Logger.LogInformation("FlutterUiNeuron handled UiSurface kind={Kind}", surface.Kind);
        await Task.CompletedTask;
    }
}
