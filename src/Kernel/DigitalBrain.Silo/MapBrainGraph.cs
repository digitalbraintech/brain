using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Kernel;

internal static class BrainGraphHttpMaps
{
    public static IEndpointRouteBuilder MapBrainGraph(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/chats/{chatName}/brain/events", static async Task (
            string chatName, HttpContext http, BrainGraphStream stream, CancellationToken cancellationToken) =>
        {
            await SseResponse.WriteAsync(http.Response,
                stream.WatchAsync(chatName, HttpActor.Current, cancellationToken), cancellationToken).ConfigureAwait(false);
        });
        endpoints.MapGet(HttpSurfacePaths.BrainGraphPath,
            static async Task<IResult> (string chatName, HttpContext http, BrainGraphProjection graph,
                CancellationToken cancellationToken) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                try
                {
                    return Results.Ok(await graph.ReadAsync(chatName, HttpActor.Current, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { message = "A valid local chat name is required." });
                }
                catch (NeuronAuthorizationException)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            });

        endpoints.MapPost(HttpSurfacePaths.BrainGraphSubscriptionsPath,
            static async Task<IResult> (string chatName, BrainGraphSubscriptionRequest request,
                BrainGraphProjection graph, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await graph.SetSubscriptionAsync(chatName, HttpActor.Current, request,
                        cancellationToken).ConfigureAwait(false));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { message = "A valid graph subscription is required." });
                }
                catch (NeuronAuthorizationException)
                {
                    return Results.Json(new { message = "The subscription is not permitted in this conversation's graph." },
                        statusCode: StatusCodes.Status403Forbidden);
                }
            });
        return endpoints;
    }
}
