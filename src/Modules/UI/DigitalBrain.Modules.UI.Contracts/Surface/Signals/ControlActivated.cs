using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.control-activated")]
public sealed record ControlActivated(
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string ControlId,
    [property: Id(2)] string Intent) : Signal;
