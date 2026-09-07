using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

// One direction of a neuron's traffic journal. Retention bounds recent deliveries;
// sequence and lifetime tallies survive compaction.
internal sealed class JournalWindow
{
    private const int MaxRetainedEntries = 512;
    private const int MaxRetainedBytes = 512 * 1024;

    private readonly IDurableList<byte[]> _retained;
    private readonly IDurableDictionary<string, long> _tallies;
    private readonly IDurableValue<long> _lastSequence;
    private readonly Serializer<JournalEntry> _entries;
    private readonly SerializerSessionPool _sessions;

    internal JournalWindow(
        IDurableList<byte[]> retained,
        IDurableDictionary<string, long> tallies,
        IDurableValue<long> lastSequence,
        Serializer<JournalEntry> entries,
        SerializerSessionPool sessions)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(tallies);
        ArgumentNullException.ThrowIfNull(lastSequence);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(sessions);

        _retained = retained;
        _tallies = tallies;
        _lastSequence = lastSequence;
        _entries = entries;
        _sessions = sessions;
    }

    internal long NextSequence => _lastSequence.Value + 1;

    internal JournalRead Read(long afterSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var lastSequence = _lastSequence.Value;

        if (afterSequence > lastSequence
            || (afterSequence < lastSequence && afterSequence < EarliestRetainedSequence() - 1))
        {
            return new(lastSequence, [], Snapshot());
        }

        var firstIndex = (int)(afterSequence - EarliestRetainedSequence() + 1);
        List<SignalDelivery> deliveries = [];
        for (var index = firstIndex; index < _retained.Count; index++)
        {
            var encoded = _retained[index];
            using var session = _sessions.GetSession();
            var reader = Reader.Create(encoded, session);
            deliveries.Add(_entries.Deserialize(ref reader).Delivery);
        }

        return new(lastSequence, deliveries, null);
    }

    internal void Append(SignalDelivery delivery)
    {
        var sequence = _lastSequence.Value + 1;
        var signalType = TallyKeyFor(delivery);

        _lastSequence.Value = sequence;
        _retained.Add(_entries.SerializeToArray(new JournalEntry(sequence, delivery)));
        _tallies[signalType] = RecordedOf(signalType) + 1;

        Compact();
    }

    internal JournalSnapshot Snapshot() => new(
        TotalRecorded: _tallies.Sum(tally => tally.Value),
        LastSequence: _lastSequence.Value,
        EarliestRetainedSequence: EarliestRetainedSequence(),
        RetainedCount: _retained.Count,
        Tallies: [.. _tallies.Select(tally => new JournalTally(tally.Key, tally.Value))]);

    private long EarliestRetainedSequence()
        => _retained.Count == 0 ? _lastSequence.Value + 1 : _lastSequence.Value - _retained.Count + 1;

    private long RecordedOf(string signalType)
        => _tallies.TryGetValue(signalType, out var recorded) ? recorded : 0;

    private static string TallyKeyFor(SignalDelivery delivery) => delivery.Signal.Type;

    private void Compact()
    {
        var retainedBytes = _retained.Sum(entry => (long)entry.Length);

        while (_retained.Count > MaxRetainedEntries
            || (retainedBytes > MaxRetainedBytes && _retained.Count > 1))
        {
            retainedBytes -= _retained[0].Length;
            _retained.RemoveAt(0);
        }
    }
}
