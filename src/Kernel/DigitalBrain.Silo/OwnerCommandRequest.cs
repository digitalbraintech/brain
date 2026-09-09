using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record OwnerCommandRequest(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("chatName")] string? ChatName = null,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("commandId")] string? CommandId = null,
    [property: JsonPropertyName("turnId")] string? TurnId = null,
    [property: JsonPropertyName("surfaceName")] string? SurfaceName = null,
    [property: JsonPropertyName("surfaceKey")] string? SurfaceKey = null,
    [property: JsonPropertyName("title")] string? Title = null);

