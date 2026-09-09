using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

// Thrown when a journal compacts past the wait cursor DURING an already-established wait —
// the deliveries are gone (ResetSnapshot semantics), so waiting further can never succeed.
// A reset observed on the wait's first read is not this: it is the wait's baseline. If the
// requested cursor was beyond the tip, the baseline is "start watching from now". If the
// requested cursor had already fallen out of retention before the wait started, the baseline
// instead adopts the earliest still-readable sequence, so the retained window gets scanned
// rather than silently skipped.
// Distinct from TimeoutException: this is "unknowable", not "not yet".
public sealed class JournalCompactedException(string message) : InvalidOperationException(message);

public static class JournalWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static Task<SignalDelivery> ForAsync<TNeuron>(
        NeuronReference<TNeuron> neuron,
        JournalKind kind,
        Func<SignalDelivery, bool> match,
        TimeSpan? timeout = null,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron
        => ForAsync(
            neuron.Id,
            kind,
            (cursor, token) => neuron.ReadJournalAsync(kind, cursor, token),
            match,
            timeout,
            afterSequence,
            cancellationToken);

    public static Task<SignalDelivery> ForAsync(
        IDigitalBrain brain,
        JournalKind kind,
        Func<SignalDelivery, bool> match,
        TimeSpan? timeout = null,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);

        return ForAsync(
            IBrainNeuron.ForOwner(brain.Owner),
            kind,
            (cursor, token) => brain.ReadJournalAsync(kind, cursor, token),
            match,
            timeout,
            afterSequence,
            cancellationToken);
    }

    public static Task<SignalDelivery> ForAsync(
        INeuronQuery neuron,
        NeuronId subject,
        JournalKind kind,
        Func<SignalDelivery, bool> match,
        TimeSpan? timeout = null,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(neuron);

        return ForAsync(
            subject,
            kind,
            (cursor, token) => neuron.ReadJournal(kind, cursor).WaitAsync(token),
            match,
            timeout,
            afterSequence,
            cancellationToken);
    }

    private static async Task<SignalDelivery> ForAsync(
        NeuronId subject,
        JournalKind kind,
        Func<long, CancellationToken, Task<JournalRead>> read,
        Func<SignalDelivery, bool> match,
        TimeSpan? timeout,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(match);

        var budget = timeout ?? DefaultTimeout;
        var deadline = DateTimeOffset.UtcNow + budget;
        var seenTypes = new List<string>();
        var isBaselineRead = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await read(afterSequence, cancellationToken).ConfigureAwait(false);

            if (page.ResetSnapshot is not null)
            {
                if (isBaselineRead)
                {
                    var snapshot = page.ResetSnapshot;
                    if (afterSequence < snapshot.EarliestRetainedSequence - 1)
                    {
                        // The requested cursor had already fallen out of retention before this
                        // wait started, but up to 512 retained entries are still readable.
                        // Adopt earliest-retained-minus-one as the baseline cursor so the NEXT
                        // poll scans the retained window instead of silently skipping straight
                        // to the tip and losing entries the wait could otherwise have seen.
                        afterSequence = snapshot.EarliestRetainedSequence - 1;
                    }
                    else
                    {
                        // The requested cursor is beyond the tip — that's the wait's baseline
                        // ("start watching from now"), not a loss. Nothing the wait promised to
                        // see has gone missing, so adopt the tip and keep polling.
                        afterSequence = page.ResumeSequence;
                    }
                }
                else
                {
                    var seenBeforeCompaction = seenTypes.Count > 0 ? string.Join(", ", seenTypes) : "(none)";
                    throw new JournalCompactedException(
                        $"The {kind} journal of {subject} compacted past the wait cursor (resume {page.ResumeSequence}) "
                        + $"mid-wait; deliveries were dropped before they could be observed. Saw before compaction: [{seenBeforeCompaction}]");
                }
            }
            else
            {
                foreach (var delivery in page.Delta)
                {
                    var typeName = delivery.Signal.GetType().Name;
                    if (!seenTypes.Contains(typeName, StringComparer.Ordinal))
                    {
                        seenTypes.Add(typeName);
                    }

                    if (match(delivery))
                    {
                        return delivery;
                    }
                }

                afterSequence = page.ResumeSequence;
            }

            isBaselineRead = false;

            if (DateTimeOffset.UtcNow >= deadline)
            {
                var seen = seenTypes.Count > 0 ? string.Join(", ", seenTypes) : "(none)";
                throw new TimeoutException(
                    $"No matching {kind} delivery on {subject} within {budget}. Saw: [{seen}]");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
