using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.UI;

public static class UIRendererEventExtensions
{
    public static UIRendererEvents Events(this NeuronReference<IUIRenderer> renderer) => new(renderer);
}

public sealed class UIRendererEvents
{
    internal UIRendererEvents(NeuronReference<IUIRenderer> renderer)
    {
        SurfaceOpened = new(renderer.ApplicationScopeId, "neuron", renderer.Id.ToString(), "",
            "surface-opened", "ui.surface-opened/v1");
        ControlActivated = new(renderer.ApplicationScopeId, "neuron", renderer.Id.ToString(), "",
            "control-activated", "ui.control-activated/v1");
        ComponentAdded = new(renderer.ApplicationScopeId, "neuron", renderer.Id.ToString(), "",
            "component-added", "ui.component-added/v1");
    }

    public EventPort<SurfaceOpened> SurfaceOpened { get; }
    public EventPort<ControlActivated> ControlActivated { get; }
    public EventPort<ComponentAdded> ComponentAdded { get; }
}
