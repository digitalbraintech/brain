using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed class NeuronJournals(JournalWindow incoming, JournalWindow outgoing)
{
    internal long OutgoingNextSequence => outgoing.NextSequence;

    internal long IncomingLastSequence => incoming.LastSequence;

    internal bool TryReadIncoming(long sequence, out SignalDelivery delivery) => incoming.TryRead(sequence, out delivery);

    internal JournalRead Read(JournalKind kind, long afterSequence) => WindowFor(kind).Read(afterSequence);

    internal void AppendIncoming(SignalDelivery delivery) => incoming.Append(delivery);

    internal void AppendOutgoing(SignalDelivery delivery) => outgoing.Append(delivery);

    private JournalWindow WindowFor(JournalKind kind) => kind switch
    {
        JournalKind.Incoming => incoming,
        JournalKind.Outgoing => outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
