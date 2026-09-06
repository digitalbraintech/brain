using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-state")]
public sealed record SurfaceState(
    [property: Id(0)] IReadOnlyList<SurfaceScene> Scenes,
    [property: Id(1)] IReadOnlyList<ActivityView>? Activities = null);

[GenerateSerializer]
[Alias("ui.surface-scene")]
public sealed record SurfaceScene(
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string Title,
    [property: Id(2)] SurfaceComponent? Root = null);

[GenerateSerializer]
[Alias("ui.surface-component")]
public sealed record SurfaceComponent(
    [property: Id(0)] string Kind,
    [property: Id(1)] string? Key = null,
    [property: Id(2)] IReadOnlyDictionary<string, string>? Properties = null,
    [property: Id(3)] IReadOnlyList<SurfaceComponent>? Children = null);
