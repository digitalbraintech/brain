using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.UI;

// Deliberately no [ClientEntryPoint] here (same wall as IChart): Read() arrives through
// IEntity<TState>'s entry point, while Open stays reachable only to an attributed,
// same-owner grain call (UIRenderer's OpenSurface handler).
[Alias("ui.surface")]
public interface ISurface : IEntity<SurfaceState>
{
    const string DefaultInstanceName = "desk";

    [Alias(nameof(Open))]
    Task Open(SurfaceScene scene, int cap);

    [Alias(nameof(ApplyActivity))]
    Task ApplyActivity(ActivityView activity, int cap);
}
