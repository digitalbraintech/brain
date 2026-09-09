using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

internal static class ActivitiesHttpMaps
{
    public static IEndpointRouteBuilder MapActivities(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/activities", static async Task<IResult> (
            HttpContext http, IDigitalBrain brain, CancellationToken cancellationToken) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            http.Response.Headers.CacheControl = "no-store";
            var activities = brain.Get<IActivities>(IActivities.DefaultInstanceName);
            return Results.Ok(await activities.RequestAsync(new ReadActivities(), cancellationToken)
                .ConfigureAwait(false));
        });

        endpoints.MapGet("/activities/events", static async Task (
            HttpContext http, IDigitalBrain brain, CancellationToken cancellationToken) =>
        {
            await SseResponse.WriteAsync(http.Response,
                WatchAsync(brain, HttpActor.Current, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        });

        return endpoints;
    }

    private static async IAsyncEnumerable<SseItem<object>> WatchAsync(
        IDigitalBrain brain,
        ActorContext actor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var verifiedActor = VerifiedActor.Enter(actor);
        var activities = brain.Get<IActivities>(IActivities.DefaultInstanceName);
        // Capture first so changes during the authoritative read are replayed by the watch.
        var before = await activities.ReadJournalAsync(JournalKind.Outgoing, long.MaxValue, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await activities.RequestAsync(new ReadActivities(), cancellationToken)
            .ConfigureAwait(false);
        var versions = snapshot.Activities.ToDictionary(activity => activity.Id, activity => activity.Version);
        yield return new SseItem<object>(snapshot, "snapshot");

        await foreach (var page in activities
            .WatchJournalAsync(JournalKind.Outgoing, before.ResumeSequence, cancellationToken)
            .ConfigureAwait(false))
        {
            if (page.ResetSnapshot is not null)
            {
                snapshot = await activities.RequestAsync(new ReadActivities(), cancellationToken)
                    .ConfigureAwait(false);
                versions = snapshot.Activities.ToDictionary(activity => activity.Id, activity => activity.Version);
                yield return new SseItem<object>(snapshot, "snapshot");
                continue;
            }

            foreach (var delivery in page.Delta)
            {
                if (delivery.Signal is ActivityChanged changed
                    && (changed.Activity.Principal is null || changed.Activity.Principal == actor.PrincipalId)
                    && (!versions.TryGetValue(changed.Activity.Id, out var version) || changed.Activity.Version > version))
                {
                    versions[changed.Activity.Id] = changed.Activity.Version;
                    yield return new SseItem<object>(changed.Activity, "activity");
                }
            }
        }
    }
}
