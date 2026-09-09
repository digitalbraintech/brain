using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Abstractions.Signals;

// Shared request semantics; routing and journal choice remain with each transport.
internal static class SignalRequestPolicy
{
    internal static CancellationTokenSource CreateBudget(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.Parse(NeuronCallTimeouts.LongRunning,
            System.Globalization.CultureInfo.InvariantCulture));
        return budget;
    }

    internal static TimeoutException TimedOut(NeuronId receiver, Signal request, Exception cause)
        => new($"Request '{request.GetType().Name}' to neuron '{receiver}' exceeded its deadline; "
            + "the remote outcome may be unknown.", cause);

    internal static void RequireHandled(NeuronId receiver, Signal request, DeliveryOutcome outcome)
    {
        switch (outcome)
        {
            case DeliveryOutcome.Handled:
                return;
            case DeliveryOutcome.Unhandled:
                throw new InvalidOperationException(
                    $"Neuron '{receiver}' did not handle request '{request.GetType().Name}'.");
            case DeliveryOutcome.Refused:
                throw new SignalDeliveryRefusedException(receiver, request.GetType());
            default:
                throw new InvalidOperationException(
                    $"Neuron '{receiver}' returned unknown delivery outcome '{outcome}'.");
        }
    }

    internal static Signal? FindResponse(
        JournalRead journal, NeuronId receiver, SignalDelivery request, Type responseType)
        => journal.Delta.FirstOrDefault(delivery => delivery.Caller == receiver
            && delivery.CausationId == request.SignalId
            && responseType.IsInstanceOfType(delivery.Signal))?.Signal;

    internal static async Task<JournalRead> RecoverRetainedAsync(
        JournalRead page, Func<long, Task<JournalRead>> read)
    {
        if (page.ResetSnapshot is not { } reset)
        {
            return page;
        }

        // A reset means the cursor expired, not necessarily the reply. Try exactly
        // one retained-window scan; a busy journal must not cause an endless chase.
        var retained = await read(reset.EarliestRetainedSequence - 1).ConfigureAwait(false);
        if (retained.ResetSnapshot is not null)
        {
            throw new InvalidOperationException(
                "The reply journal compacted again during its bounded retained-window scan; "
                + "the response could not be established.");
        }

        return retained;
    }

    internal static InvalidOperationException MissingResponse(
        NeuronId receiver, SignalDelivery request, Type responseType, bool compacted)
        => new(compacted
            ? $"Neuron '{receiver}' compacted its journal and no retained '{responseType.Name}' "
                + $"reply to request '{request.SignalId}' could be read."
            : $"Neuron '{receiver}' handled request '{request.SignalId}' without recording "
                + $"a '{responseType.Name}' reply with its exact causation ID.");
}
