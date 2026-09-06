using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.UI;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

internal static class SurfaceActivitiesHttpMaps
{
    public static IEndpointRouteBuilder MapSurfaceActivities(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/surfaces/{surfaceName}/activities", static async Task<IResult> (
            string surfaceName, HttpContext http, IDigitalBrain brain, CancellationToken cancellationToken) =>
        {
            if (!ValidName(surfaceName))
            {
                return Results.BadRequest();
            }
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await Snapshot(brain, surfaceName, HttpActor.Current.PrincipalId));
        });
        endpoints.MapGet("/surfaces/{surfaceName}/activities/events", static async Task (
            string surfaceName, HttpContext http, IDigitalBrain brain, CancellationToken cancellationToken) =>
        {
            if (!ValidName(surfaceName))
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await SseResponse.WriteAsync(http.Response,
                Watch(brain, surfaceName, HttpActor.Current, cancellationToken), cancellationToken).ConfigureAwait(false);
        });
        return endpoints;
    }

    private static bool ValidName(string name)
        => !string.IsNullOrWhiteSpace(name) && !name.Contains('/') && !name.Any(char.IsWhiteSpace)
            && !PrincipalPartition.TryParse(name, out _, out _);

    private static string RendererName(string name, PrincipalId principal)
        => name == ISurface.DefaultInstanceName ? name : PrincipalScoped.InstanceName(principal, name);

    private static async Task<ActivitiesSnapshot> Snapshot(IDigitalBrain brain, string name, PrincipalId principal)
    {
        var state = await brain.GetEntity<ISurface>(RendererName(name, principal)).Read().ConfigureAwait(false);
        return new ActivitiesSnapshot(DateTimeOffset.UtcNow,
            (state?.Activities ?? []).Where(activity => activity.Principal is null || activity.Principal == principal).ToArray());
    }

    private static async IAsyncEnumerable<SseItem<object>> Watch(IDigitalBrain brain, string name, ActorContext actor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var verified = VerifiedActor.Enter(actor);
        var renderer = brain.Get<IUIRenderer>(RendererName(name, actor.PrincipalId));
        var before = await renderer.ReadJournalAsync(JournalKind.Outgoing, long.MaxValue, cancellationToken).ConfigureAwait(false);
        var snapshot = await Snapshot(brain, name, actor.PrincipalId);
        var versions = snapshot.Activities.ToDictionary(activity => activity.Id, activity => activity.Version, StringComparer.Ordinal);
        yield return new SseItem<object>(snapshot, "snapshot");
        await foreach (var page in renderer.WatchJournalAsync(JournalKind.Outgoing, before.ResumeSequence, cancellationToken).ConfigureAwait(false))
        {
            if (page.ResetSnapshot is not null)
            {
                snapshot = await Snapshot(brain, name, actor.PrincipalId);
                versions = snapshot.Activities.ToDictionary(activity => activity.Id, activity => activity.Version, StringComparer.Ordinal);
                yield return new SseItem<object>(snapshot, "snapshot");
                continue;
            }
            foreach (var delivery in page.Delta)
            {
                if (delivery.Signal is ActivityChanged change
                    && (change.Activity.Principal is null || change.Activity.Principal == actor.PrincipalId))
                {
                    if (change.Activity.Version > 0 && versions.TryGetValue(change.Activity.Id, out var version)
                        && change.Activity.Version <= version)
                    {
                        continue;
                    }
                    versions[change.Activity.Id] = change.Activity.Version;
                    yield return new SseItem<object>(change.Activity, "activity");
                }
            }
        }
    }
}
