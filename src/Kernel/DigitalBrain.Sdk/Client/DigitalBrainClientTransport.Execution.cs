using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions;

internal sealed partial class DigitalBrainClientTransport
{
    private readonly NeuronId? _executionSource;
    private readonly BehaviorClaim? _claim;
    private readonly ConcurrentDictionary<string, int> _ordinals = new(StringComparer.Ordinal);

    internal DigitalBrainClientTransport(IGrainFactory grains, OwnerId owner, NeuronId source, BehaviorClaim claim)
        : this(grains, owner)
    {
        if (source.Owner != owner)
        {
            throw new NeuronAuthorizationException("An execution source must belong to the connected owner.");
        }
        _executionSource = source;
        _claim = claim;
    }

    internal Signal? Input => _claim?.Input.Signal;
    internal NeuronId? InputSource => _claim?.Input.Caller;

    private async Task<SignalDeliveryResult> SendExecutionAsync(
        NeuronId receiver, Signal signal, CancellationToken cancellationToken)
    {
        var (key, hash) = NextOperation(receiver, signal);
        var kernel = ExecutionKernel();
        var delivery = await kernel.PrepareRequest(_claim!, key, hash, receiver, signal)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var outcome = await _grains.GetGrain<INeuronGrain>(receiver.ToGrainId())
            .Deliver(delivery, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (outcome == DeliveryOutcome.Handled)
        {
            await kernel.CompleteRequest(_claim!, key, hash).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        return new SignalDeliveryResult(delivery, outcome);
    }

    private async Task<Signal> RequestExecutionAsync(
        NeuronId receiver, Signal signal, Type responseType, CancellationToken cancellationToken)
    {
        RequireOwnedSubject(receiver);
        var (key, hash) = NextOperation(receiver, signal);
        var kernel = ExecutionKernel();
        var checkpoint = signal is ICheckpointedRequest
            ? await kernel.ReadCheckpoint(_claim!, key, hash).WaitAsync(cancellationToken).ConfigureAwait(false)
            : null;
        if (checkpoint is not null)
        {
            if (!responseType.IsInstanceOfType(checkpoint.Response))
            {
                throw new InvalidOperationException("A checkpoint response does not match the requested signal type.");
            }
            await kernel.CompleteRequest(_claim!, key, hash).WaitAsync(cancellationToken).ConfigureAwait(false);
            return checkpoint.Response;
        }

        var query = _grains.GetGrain<INeuronQuery>(receiver.ToGrainId());
        // Read retained replies first on retry. A target may have finished before the
        // executor stopped, but before its checkpoint was durably acknowledged.
        var delivery = await kernel.PrepareRequest(_claim!, key, hash, receiver, signal)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var read = await query.ReadJournal(JournalKind.Outgoing, 0).WaitAsync(cancellationToken).ConfigureAwait(false);
        var retained = await SignalRequestPolicy.RecoverRetainedAsync(read,
            after => query.ReadJournal(JournalKind.Outgoing, after).WaitAsync(cancellationToken)).ConfigureAwait(false);
        var response = SignalRequestPolicy.FindResponse(retained, receiver, delivery, responseType);
        if (response is null)
        {
            var outcome = await _grains.GetGrain<INeuronGrain>(receiver.ToGrainId())
                .Deliver(delivery, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            SignalRequestPolicy.RequireHandled(receiver, signal, outcome);
            var cursor = retained.ResumeSequence;
            while (response is null)
            {
                read = await query.ReadJournal(JournalKind.Outgoing, cursor).WaitAsync(cancellationToken).ConfigureAwait(false);
                retained = await SignalRequestPolicy.RecoverRetainedAsync(read,
                    after => query.ReadJournal(JournalKind.Outgoing, after).WaitAsync(cancellationToken)).ConfigureAwait(false);
                response = SignalRequestPolicy.FindResponse(retained, receiver, delivery, responseType);
                cursor = retained.ResumeSequence;
                if (response is null)
                {
                    await Task.Delay(ResponsePollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        if (signal is ICheckpointedRequest)
        {
            await kernel.StoreCheckpoint(_claim!, new BehaviorCheckpoint(key, hash, response))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        await kernel.CompleteRequest(_claim!, key, hash).WaitAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }

    private IBehaviorKernel ExecutionKernel()
        => _grains.GetGrain<IBehaviorKernel>(_executionSource!.Value.ToGrainId());

    private (string Key, string Hash) NextOperation(NeuronId receiver, Signal signal)
    {
        var prefix = $"{receiver}/{signal.GetType().FullName}";
        if (signal is not ICheckpointedRequest)
        {
            prefix = $"attempt-{_claim!.Attempt}/{prefix}";
        }
        var ordinal = _ordinals.AddOrUpdate(prefix, 0, static (_, previous) => checked(previous + 1));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(signal, signal.GetType()));
        return ($"{prefix}/{ordinal}", Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
