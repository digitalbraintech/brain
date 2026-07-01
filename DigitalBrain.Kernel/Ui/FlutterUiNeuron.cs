using DigitalBrain.Core;
using DigitalBrain.UiKit;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.flutter-ui.v1")]
public sealed class FlutterUiNeuron : Neuron, IFlutterUiNeuron
{
    public FlutterUiNeuron(ILogger<FlutterUiNeuron> logger, NeuronJournals journals)
        : base(logger, journals) { }

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
