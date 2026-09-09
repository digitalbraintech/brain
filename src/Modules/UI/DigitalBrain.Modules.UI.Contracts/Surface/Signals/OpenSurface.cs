using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.open-surface")]
[DigitalBrain.Abstractions.Scripting.ApplicationJsonContract("ui.open-surface", 1)]
public sealed record OpenSurface : Signal
{
    // Overrides IUIRenderer's own default ("default"): an untargeted fire must still reach the
    // "desk" surface the startup script opens and the shell watches, even though the renderer serves
    // other capabilities (ChartPoint) that keep the neuron-level default.
    public const string DefaultInstanceName = ISurface.DefaultInstanceName;

    public OpenSurface(CommandId commandId, string surfaceKey, string title, SurfaceComponent? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        CommandId = commandId;
        SurfaceKey = surfaceKey;
        Title = title;
        Root = root;
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public string SurfaceKey { get; init; }

    [Id(2)]
    public string Title { get; init; }

    [Id(3)]
    public SurfaceComponent? Root { get; init; }
}
