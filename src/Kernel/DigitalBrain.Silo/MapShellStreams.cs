using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Abstractions;
using DigitalBrain.UI;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Kernel;

internal static class SurfaceStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapSurfaceStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.SurfaceEventsPath,
            static async Task (
                HttpContext http,
                string surfaceName,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                var actor = HttpActor.Current;

                if (string.IsNullOrWhiteSpace(surfaceName)
                    || !TryPrincipalResource(actor.PrincipalId, surfaceName, out var surfaceInstance))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    surfaceName == ISurface.DefaultInstanceName
                        ? WatchDefaultSurfaceAsync(sessionJournal, surfaceInstance, cursor, cancellationToken)
                        : WatchSurfaceOpenedAsync(sessionJournal, surfaceInstance, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static bool TryPrincipalResource(PrincipalId principal, string localName, out string instanceName)
    {
        try
        {
            instanceName = PrincipalSurface.InstanceName(principal, localName);
            return true;
        }
        catch (ArgumentException)
        {
            instanceName = "";
            return false;
        }
    }

    private static IAsyncEnumerable<SseItem<SurfaceOpenedEvent>> WatchSurfaceOpenedAsync(
        OwnerSessionJournal sessionJournal,
        string surfaceName,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchSurfaceOutgoingAsync(surfaceName, afterSequence, token),
            HttpSurfacePaths.SurfaceOpenedEvent,
            ProjectSurfaceOpened,
            cancellationToken);

    private static async IAsyncEnumerable<SseItem<SurfaceOpenedEvent>> WatchDefaultSurfaceAsync(
        OwnerSessionJournal journal, string principalInstance, long cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<SseItem<SurfaceOpenedEvent>>(32);
        // The scripted scene belongs to the owner; legacy opened cards belong to the viewer.
        // Each journal has its own cursor. Replaying shared composition is idempotent.
        var writers = Task.WhenAll(Copy(principalInstance, cursor), Copy(ISurface.DefaultInstanceName, 0));
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            await lifetime.CancelAsync();
            try { await writers.ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }

        async Task Copy(string name, long after)
        {
            try
            {
                await foreach (var item in WatchSurfaceOpenedAsync(journal, name, after, lifetime.Token).ConfigureAwait(false))
                {
                    // Only the principal journal advances the reconnect cursor. Shared scene
                    // notifications always replay, so their independent sequence must not skip
                    // principal events when the client reconnects.
                    var projected = name == ISurface.DefaultInstanceName
                        ? new SseItem<SurfaceOpenedEvent>(item.Data with { Sequence = 0 }, item.EventType)
                        : item;
                    await channel.Writer.WriteAsync(projected, lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
                throw;
            }
        }
    }

    private static SurfaceOpenedEvent? ProjectSurfaceOpened(SignalDelivery delivery)
        => delivery.Signal is not SurfaceOpened opened
            ? null
            : new SurfaceOpenedEvent(
                delivery.Sequence,
                opened.SurfaceKey,
                opened.Title,
                opened.CommandId.ToString(),
                opened.Surface.ToString());
}
