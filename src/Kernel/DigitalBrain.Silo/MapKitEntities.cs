using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Excel;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class KitEntitiesHttpMaps
{
    public static IEndpointRouteBuilder MapKitEntities(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.KitChartPath,
            static async Task<IResult> (string chartName, IDigitalBrain brain, CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(chartName)
                    || !TryPrincipalResource(HttpActor.Current.PrincipalId, chartName, out var instance))
                {
                    return Results.BadRequest();
                }

                var state = await brain.GetEntity<IChart>(instance).Read().ConfigureAwait(false);
                return state is null ? Results.NotFound() : Results.Ok(state);
            });

        endpoints.MapGet(
            HttpSurfacePaths.KitImagePath,
            static async Task<IResult> (string imageName, IDigitalBrain brain, CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(imageName)
                    || !TryPrincipalResource(HttpActor.Current.PrincipalId, imageName, out var instance))
                {
                    return Results.BadRequest();
                }

                var state = await brain.GetEntity<IImage>(instance).Read().ConfigureAwait(false);
                return state is null
                    ? Results.NotFound()
                    : Results.Ok(new KitImageStateResponse(state.Prompt, state.Model, state.MediaType));
            });

        endpoints.MapGet(
            HttpSurfacePaths.KitImageContentPath,
            static async Task<IResult> (
                string imageName,
                IDigitalBrain brain,
                IKitImageStore imageStore,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(imageStore);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(imageName)
                    || !TryPrincipalResource(HttpActor.Current.PrincipalId, imageName, out var instance))
                {
                    return Results.BadRequest();
                }

                var state = await brain.GetEntity<IImage>(instance).Read().ConfigureAwait(false);
                if (state is null)
                {
                    return Results.NotFound();
                }

                var blob = await imageStore.ReadAsync(state.BlobName, cancellationToken).ConfigureAwait(false);
                return blob is null ? Results.NotFound() : Results.File(blob.Value.Content, blob.Value.MediaType);
            });

        endpoints.MapGet(
            HttpSurfacePaths.KitSpreadsheetPath,
            static async Task<IResult> (string spreadsheetName, IDigitalBrain brain, CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(spreadsheetName)
                    || !TryPrincipalResource(HttpActor.Current.PrincipalId, spreadsheetName, out var instance))
                {
                    return Results.BadRequest();
                }

                var state = await brain.GetEntity<IExcel>(instance).Read().ConfigureAwait(false);
                return state is null ? Results.NotFound() : Results.Ok(state);
            });

        endpoints.MapGet(
            HttpSurfacePaths.KitGraphPath,
            static async Task<IResult> (string graphName, IDigitalBrain brain, CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(graphName)
                    || !TryPrincipalResource(HttpActor.Current.PrincipalId, graphName, out var instance))
                {
                    return Results.BadRequest();
                }

                var state = await brain.GetEntity<IGraph>(instance).Read().ConfigureAwait(false);
                return state is null ? Results.NotFound() : Results.Ok(state);
            });

        endpoints.MapGet(
            HttpSurfacePaths.KitSurfacePath,
            static async Task<IResult> (string surfaceName, IDigitalBrain brain, CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(surfaceName)
                    || !TryPrincipalResource(HttpActor.Current.PrincipalId, surfaceName, out var instance))
                {
                    return Results.BadRequest();
                }

                var state = await brain.GetEntity<ISurface>(instance).Read().ConfigureAwait(false);
                return state is null ? Results.NotFound() : Results.Ok(state);
            });

        return endpoints;
    }

    private static bool TryPrincipalResource(PrincipalId principal, string localName, out string instanceName)
    {
        try
        {
            instanceName = PrincipalScoped.InstanceName(principal, localName);
            return true;
        }
        catch (ArgumentException)
        {
            instanceName = "";
            return false;
        }
    }
}

// Projects ImageState onto the wire without BlobName -- the blob key is a storage
// implementation detail, not part of the kit card's client-facing contract.
internal sealed record KitImageStateResponse(string Prompt, string Model, string MediaType);
