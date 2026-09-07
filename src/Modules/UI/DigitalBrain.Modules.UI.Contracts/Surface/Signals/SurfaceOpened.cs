using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-opened")]
public sealed record SurfaceOpened(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Surface,
    [property: Id(2)] string SurfaceKey,
    [property: Id(3)] string Title) : Signal;
