using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Chat;
using DigitalBrain.Execution;

namespace DigitalBrain.Kernel;

// A read adapter to existing kernel interfaces. It does not retain topology or run
// background work. The interface keeps graph scope/redaction testable without a silo.
internal interface IBrainGraphSource
{
    OwnerId Owner { get; }
    Task<NeuronId?> ReadActiveExecutionAsync(NeuronId chat, CancellationToken cancellationToken);
    Task<BrainGraphNeuronRead> ReadAsync(NeuronId neuron, CancellationToken cancellationToken);
    Task<DeliveryOutcome> SendAsync(NeuronId receiver, Signal signal, CancellationToken cancellationToken);
}

internal sealed record BrainGraphNeuronRead(
    IReadOnlyList<Synapse> Synapses,
    JournalRead Incoming,
    JournalRead Outgoing);

internal sealed class BrainGraphSource(IGrainFactory grains, IDigitalBrain brain) : IBrainGraphSource
{
    private const int RecentDeliveries = 12;

    public OwnerId Owner => brain.Owner;

    public async Task<NeuronId?> ReadActiveExecutionAsync(NeuronId chat, CancellationToken cancellationToken)
    {
        var execution = await grains.GetGrain<IChatKernel>(chat.ToGrainId())
            .LoadActiveExecution().WaitAsync(cancellationToken).ConfigureAwait(false);
        return execution is { } id ? NeuronId.For<IExecution>(Owner, id.ToString()) : null;
    }

    public async Task<BrainGraphNeuronRead> ReadAsync(NeuronId neuron, CancellationToken cancellationToken)
    {
        RequireOwner(neuron);
        var query = grains.GetGrain<INeuronQuery>(neuron.ToGrainId());
        var synapses = query.ReadSynapses().WaitAsync(cancellationToken);
        var incoming = ReadRecentAsync(query, JournalKind.Incoming, cancellationToken);
        var outgoing = ReadRecentAsync(query, JournalKind.Outgoing, cancellationToken);
        await Task.WhenAll(synapses, incoming, outgoing).ConfigureAwait(false);
        return new(await synapses.ConfigureAwait(false),
            await incoming.ConfigureAwait(false), await outgoing.ConfigureAwait(false));
    }

    public async Task<DeliveryOutcome> SendAsync(
        NeuronId receiver, Signal signal, CancellationToken cancellationToken)
    {
        RequireOwner(receiver);
        // Preserve the ordinary root -> subscriber -> source domain path. In particular,
        // never call BindOutgoing directly from HTTP and bypass target IHandle validation.
        var result = await grains.GetGrain<IBrainNeuron>(IBrainNeuron.ForOwner(Owner).ToGrainId())
            .Send(receiver, signal).WaitAsync(cancellationToken).ConfigureAwait(false);
        return result.Outcome;
    }

    private void RequireOwner(NeuronId neuron)
    {
        if (neuron.Owner != Owner)
        {
            throw new NeuronAuthorizationException("The graph cannot access a different owner.");
        }
    }

    private static async Task<JournalRead> ReadRecentAsync(
        INeuronQuery query, JournalKind kind, CancellationToken cancellationToken)
    {
        var head = await query.ReadJournal(kind, long.MaxValue)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var cursor = Math.Max(0, head.ResumeSequence - RecentDeliveries);
        if (head.ResetSnapshot is { } snapshot)
        {
            cursor = Math.Max(cursor, snapshot.EarliestRetainedSequence - 1);
        }

        var read = await query.ReadJournal(kind, cursor)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        // A busy journal may compact between the two reads. Retry once from its new
        // retained tail, then let the next snapshot recover if it races again.
        if (read.ResetSnapshot is { } reset)
        {
            read = await query.ReadJournal(kind,
                Math.Max(reset.EarliestRetainedSequence - 1, reset.LastSequence - RecentDeliveries))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return read;
    }
}
