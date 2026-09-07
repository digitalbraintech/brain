using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-state")]
public sealed record SurfaceState(
    [property: Id(0)] IReadOnlyList<SurfaceScene> Scenes,
    [property: Id(1)] IReadOnlyList<ActivityView>? Activities = null,
    [property: Id(2)] IReadOnlyList<SurfaceOpenReceipt>? OpenReceipts = null);

[GenerateSerializer]
[Alias("ui.surface-open-receipt")]
public sealed record SurfaceOpenReceipt(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Fingerprint,
    [property: Id(2)] IReadOnlyList<SurfaceComponent> AddedComponents);

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
