using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record SurfaceOpenedEvent(
    long Sequence,
    string SurfaceKey,
    string Title,
    string CommandId,
    string Surface);

