using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Kernel;

internal static class JournalProjection
{
    public static async IAsyncEnumerable<SseItem<TEvent>> WatchAsync<TEvent>(
        Func<CancellationToken, IAsyncEnumerable<JournalRead>> openOutgoing,
        string eventName,
        Func<SignalDelivery, TEvent?> project,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(openOutgoing);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(project);

        await foreach (var page in openOutgoing(cancellationToken).ConfigureAwait(false))
        {
            if (page.ResetSnapshot is not null)
            {
                continue;
            }

            foreach (var delivery in page.Delta)
            {
                if (project(delivery) is not { } projected)
                {
                    continue;
                }

                yield return new SseItem<TEvent>(projected, eventName)
                {
                    EventId = delivery.Sequence.ToString(CultureInfo.InvariantCulture),
                };
            }
        }
    }
}
