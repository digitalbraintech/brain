using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

[GrainType("application-call-source")]
internal sealed class ApplicationCallSource(NeuronRuntime runtime) : Neuron(runtime), IApplicationCallSource
{
    public async Task<SignalDelivery> Prepare(
        Signal request, SignalId signalId, CorrelationId correlation, long sourceEpoch)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceEpoch);
        var actor = VerifiedActor.Current ?? throw new InvalidOperationException("An application call needs an authenticated principal.");
        if (!PrincipalPartition.OwnsInstance(actor.PrincipalId, Id.Name))
        {
            throw new InvalidOperationException("The application call source belongs to another principal.");
        }
        var journal = await ReadJournal(JournalKind.Outgoing, 0).ConfigureAwait(true);
        var existing = journal.Delta.SingleOrDefault(item => item.SignalId == signalId);
        if (existing is not null)
        {
            var sameRequest = existing.Signal.GetType() == request.GetType()
                && System.Text.Json.JsonSerializer.Serialize(existing.Signal, existing.Signal.GetType())
                    == System.Text.Json.JsonSerializer.Serialize(request, request.GetType());
            if (!sameRequest || existing.CorrelationId != correlation || existing.Principal != actor.PrincipalId
                || existing.SourceEpoch != sourceEpoch)
            {
                throw new InvalidOperationException("An application call identity cannot be reused with different input.");
            }
            return existing;
        }
        if (journal.Delta.Count != 0)
        {
            throw new InvalidOperationException("An application call source can prepare only its single named effect.");
        }
        var delivery = CreateDelivery(request, signalId: signalId, sourceEpoch: sourceEpoch, correlation: correlation);
        return await RecordOutgoingAsync(delivery).ConfigureAwait(true);
    }
}
