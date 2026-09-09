using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class ActivityResultsHttpMaps
{
    public static IEndpointRouteBuilder MapActivityResults(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/surfaces/{surfaceName}/activities/{activityId}/results", static async Task<IResult> (
            string surfaceName, string activityId, HttpContext http, IDigitalBrain brain, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            var actor = HttpActor.Current;
            using var verified = VerifiedActor.Enter(actor);
            if (string.IsNullOrWhiteSpace(surfaceName) || surfaceName.Any(char.IsWhiteSpace) || surfaceName.Contains('/')
                || PrincipalPartition.TryParse(surfaceName, out _, out _) || !Guid.TryParse(activityId, out _))
            {
                return Results.BadRequest();
            }
            var name = surfaceName == ISurface.DefaultInstanceName ? surfaceName : PrincipalScoped.InstanceName(actor.PrincipalId, surfaceName);
            var surface = await brain.GetEntity<ISurface>(name).Read().WaitAsync(cancellationToken);
            var activity = surface?.Activities?.FirstOrDefault(item => item.Id == activityId
                && (item.Principal is null || item.Principal == actor.PrincipalId));
            if (activity is null)
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var deliveries = await ReadParticipantsAsync(activity.ParticipantNeuronIds, async (id, token) =>
                {
                    var separator = id.IndexOf(':');
                    if (separator <= 0)
                    {
                        return Array.Empty<SignalDelivery>();
                    }
                    var neuron = new NeuronId(id[..separator], brain.Owner, id[(separator + 1)..]);
                    if (PrincipalPartition.TryParse(neuron.Name, out var principal, out _) && principal != actor.PrincipalId)
                    {
                        return Array.Empty<SignalDelivery>();
                    }
                    var query = grains.GetGrain<INeuronQuery>(neuron.ToGrainId());
                    var incoming = await ReadRetainedAsync(cursor => query.ReadJournal(JournalKind.Incoming, cursor), token);
                    var outgoing = await ReadRetainedAsync(cursor => query.ReadJournal(JournalKind.Outgoing, cursor), token);
                    return incoming.Concat(outgoing).ToArray();
                }, cancellationToken);
                return Results.Ok(Project(deliveries, activity, actor.PrincipalId));
            }
            catch (ActivityResultsUnavailableException)
            {
                return Results.Problem("The activity journals changed while reading their results. Retry the request.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        return endpoints;
    }

    internal static async Task<IReadOnlyList<SignalDelivery>> ReadRetainedAsync(Func<long, Task<JournalRead>> read,
        CancellationToken cancellationToken)
    {
        var cursor = 0L;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var page = await read(cursor).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (page.ResetSnapshot is not { } reset)
            {
                return page.Delta;
            }
            cursor = Math.Max(0, reset.EarliestRetainedSequence - 1);
        }
        throw new ActivityResultsUnavailableException();
    }

    internal static async Task<IReadOnlyList<SignalDelivery>> ReadParticipantsAsync(IEnumerable<string> participants,
        Func<string, CancellationToken, Task<IReadOnlyList<SignalDelivery>>> read, CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<IReadOnlyList<SignalDelivery>>();
        await Parallel.ForEachAsync(participants.Distinct(StringComparer.Ordinal), new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = cancellationToken,
        }, async (id, token) => results.Add(await read(id, token).ConfigureAwait(false))).ConfigureAwait(false);
        return results.SelectMany(items => items).ToArray();
    }

    internal static IReadOnlyList<ChatTurnEvent> Project(IEnumerable<SignalDelivery> deliveries, ActivityView activity, PrincipalId principal)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ChatTurnEvent>();
        foreach (var delivery in InCausalOrder(deliveries.Where(item => !item.IsActivityTelemetry
            && item.CorrelationId.ToString() == activity.CorrelationId
            && (item.Principal == principal || activity.Principal is null && item.Principal is null))))
        {
            var signalId = delivery.SignalId.ToString();
            var key = delivery.Signal is UserMessaged message ? "input:" + message.CommandId : signalId;
            if (!seen.Add(key))
            {
                continue;
            }
            // This is a snapshot identity, not a journal cursor. Keep it exact on web.
            var sequence = (BitConverter.ToInt64(delivery.SignalId.Value.ToByteArray()) & ((1L << 52) - 1)) | (1L << 52);
            ChatTurnEvent Row(bool fromUser, string text, string command, string signal, string? turn = null,
                KitCardOffer[]? cards = null, DigitalBrain.Product.Interactions.UserActionRequest? action = null)
                => new(sequence, fromUser, text, command, signal, delivery.Caller.ToString(), delivery.Caller.ToString(),
                    activity.CorrelationId, delivery.Timestamp, turn, Cards: cards, UserAction: action, EventId: signalId);
            var row = delivery.Signal switch
            {
                UserMessaged input => Row(true, input.Text, input.CommandId.ToString(), nameof(UserMessaged)),
                Responded response => Row(false, response.Text, response.CommandId.ToString(), nameof(Responded),
                    response.TurnId?.ToString(), response.Cards, response.UserAction),
                Note note => Row(false, note.Text, activity.CommandId ?? signalId, nameof(Note)),
                _ => null,
            };
            if (row is not null)
            {
                result.Add(row);
            }
        }
        return result;
    }

    private static IEnumerable<SignalDelivery> InCausalOrder(IEnumerable<SignalDelivery> deliveries)
    {
        var remaining = deliveries.DistinctBy(item => item.SignalId).ToDictionary(item => item.SignalId);
        var children = remaining.Values.Where(item => item.CausationId is not null).ToLookup(item => item.CausationId!.Value);
        var order = Comparer<SignalDelivery>.Create((first, second) =>
        {
            var timestamp = first.Timestamp.CompareTo(second.Timestamp);
            return timestamp != 0 ? timestamp : string.CompareOrdinal(first.SignalId.ToString(), second.SignalId.ToString());
        });
        var ready = new SortedSet<SignalDelivery>(remaining.Values.Where(item =>
            item.CausationId is not { } parent || !remaining.ContainsKey(parent)), order);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            remaining.Remove(next.SignalId);
            yield return next;
            foreach (var child in children[next.SignalId])
            {
                ready.Add(child);
            }
        }
        // Malformed cyclic metadata must not hide otherwise readable results.
        foreach (var item in remaining.Values.Order(order))
        {
            yield return item;
        }
    }
}

internal sealed class ActivityResultsUnavailableException : Exception;
