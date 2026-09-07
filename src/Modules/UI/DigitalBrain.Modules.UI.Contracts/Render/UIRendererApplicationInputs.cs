using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.UI;

public static class UIRendererApplicationInputExtensions
{
    public static UIRendererInputs Inputs(this NeuronReference<IUIRenderer> renderer) => new(renderer);
}

public sealed class UIRendererInputs
{
    internal UIRendererInputs(NeuronReference<IUIRenderer> renderer)
        => ActivityChanged = new(renderer.ApplicationScopeId, renderer.Id, "activity.changed/v1");
    public InputPort<ActivityChanged> ActivityChanged { get; }
}
