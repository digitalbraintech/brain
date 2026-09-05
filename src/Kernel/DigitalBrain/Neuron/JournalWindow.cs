using DigitalBrain.Abstractions;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;

using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Core;

// One direction of a neuron's traffic journal. Retention bounds recent deliveries;
// sequence and lifetime tallies survive compaction.
internal sealed class JournalWindow
{
    private const int MaxRetainedEntries = 512;
    private const int MaxRetainedBytes = 512 * 1024;
    private static readonly string DigitalBrainActivatedTallyKey =
        string.Concat("DigitalBrain.Abstractions.", "Messaging.DigitalBrainActivated");

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
        List<UnknownJournalEntry> unknown = [];
        for (var index = firstIndex; index < _retained.Count; index++)
        {
            var encoded = _retained[index];
            using var session = _sessions.GetSession();
            var reader = Reader.Create(encoded, session);
            try
            {
                deliveries.Add(_entries.Deserialize(ref reader).Delivery);
            }
            catch (FieldTypeMissingException) when (HasUnresolvedTypeAt(encoded, reader.Position))
            {
                // Do not rewrite the bytes: a future runtime may know this contract.
                // Malformed encoding and unrelated deserialization failures stay errors.
                unknown.Add(new(EarliestRetainedSequence() + index, encoded.Length));
            }
        }

        return new(
            ResumeSequence: lastSequence,
            Delta: deliveries,
            ResetSnapshot: null,
            UnknownEntries: unknown.Count == 0 ? null : unknown);
    }

    private bool HasUnresolvedTypeAt(byte[] encoded, long failedPosition)
    {
        // Orleans reports both absent type metadata and unresolved encoded names as
        // FieldTypeMissingException. Recognize only an explicit unresolved header at
        // the failed read position, then validate the whole entry's wire framing.
        using var session = _sessions.GetSession();
        var reader = Reader.Create(encoded, session);
        var unresolved = false;
        var depth = 0;
        do
        {
            var field = reader.ReadFieldHeader();
            if (field.IsEndObject)
            {
                if (depth == 0)
                {
                    return false;
                }

                depth--;
            }
            else if (field.IsEndBaseFields)
            {
                if (depth == 0)
                {
                    return false;
                }
            }
            else
            {
                unresolved |= field.SchemaType == SchemaType.Encoded
                    && field.FieldType is null
                    && reader.Position == failedPosition;
                if (field.WireType == WireType.TagDelimited)
                {
                    depth++;
                }
                else
                {
                    reader.ConsumeUnknownField(field);
                }
            }
        }
        while (depth > 0);

        return unresolved && reader.Position == reader.Length;
    }

    internal long NextSequence => _lastSequence.Value + 1;

    internal JournalWindowCheckpoint Checkpoint() => new(
        [.. _retained],
        _tallies.ToDictionary(entry => entry.Key, entry => entry.Value),
        _lastSequence.Value);

    internal void Append(SignalDelivery delivery)
    {
        var sequence = _lastSequence.Value + 1;
        var signalType = TallyKeyFor(delivery.Signal);

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

    internal void Restore(JournalWindowCheckpoint checkpoint)
    {
        while (_retained.Count > 0)
        {
            _retained.RemoveAt(_retained.Count - 1);
        }

        foreach (var entry in checkpoint.Retained)
        {
            _retained.Add(entry);
        }

        foreach (var key in _tallies.Select(entry => entry.Key).ToArray())
        {
            _tallies.Remove(key);
        }

        foreach (var tally in checkpoint.Tallies)
        {
            _tallies[tally.Key] = tally.Value;
        }

        _lastSequence.Value = checkpoint.LastSequence;
    }

    private long EarliestRetainedSequence()
        => _retained.Count == 0 ? _lastSequence.Value + 1 : _lastSequence.Value - _retained.Count + 1;

    private long RecordedOf(string signalType)
        => _tallies.TryGetValue(signalType, out var recorded) ? recorded : 0;

    // Journal tally keys are persisted protocol data. Keep the historical activation key so
    // moving the CLR type into Signals does not split one brain's counter across two names.
    private static string TallyKeyFor(Signal signal)
        => signal is DigitalBrainActivated
            ? DigitalBrainActivatedTallyKey
            : signal.GetType().FullName!;

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
