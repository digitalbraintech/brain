using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.component-added")]
[ApplicationJsonContract("ui.component-added", 1)]
public sealed record ComponentAdded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Surface,
    [property: Id(2)] string SurfaceKey,
    [property: Id(3)] SurfaceComponent Component) : Signal;
