using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class SurfaceControlHttpMaps
{
    public static IEndpointRouteBuilder MapSurfaceControls(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/surfaces/{surfaceName}/controls/{controlId}/activate",
            static async Task<IResult> (string surfaceName, string controlId,
                ActivateSurfaceControl request, IDigitalBrain brain, CancellationToken cancellationToken) =>
            {
                if (!Valid(surfaceName) || !Valid(controlId) || !Valid(request.SurfaceKey)
                    || string.IsNullOrWhiteSpace(request.Intent))
                {
                    return Results.BadRequest();
                }
                using var actor = VerifiedActor.Enter(HttpActor.Current);
                var state = await brain.GetEntity<ISurface>(surfaceName).Read().WaitAsync(cancellationToken);
                var scene = state?.Scenes.SingleOrDefault(candidate => candidate.SurfaceKey == request.SurfaceKey);
                var control = Find(scene?.Root, controlId);
                if (control is null)
                {
                    return Results.NotFound();
                }
                var enabled = control.Properties is null
                    || !control.Properties.TryGetValue("enabled", out var configured)
                    || !string.Equals(configured, "false", StringComparison.OrdinalIgnoreCase);
                if (!enabled || control.Properties?.GetValueOrDefault("intent") != request.Intent)
                {
                    return Results.Conflict();
                }
                await brain.Get<IUIRenderer>(surfaceName)
                    .SendAsync(new ControlActivated(request.SurfaceKey, controlId, request.Intent), cancellationToken);
                return Results.Accepted();
            });
        return endpoints;
    }

    private static SurfaceComponent? Find(SurfaceComponent? component, string controlId)
    {
        if (component is null)
        {
            return null;
        }
        if (component.Kind == "button" && component.Key == controlId)
        {
            return component;
        }
        foreach (var child in component.Children ?? [])
        {
            if (Find(child, controlId) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static bool Valid(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private sealed record ActivateSurfaceControl(string SurfaceKey, string Intent);
}
