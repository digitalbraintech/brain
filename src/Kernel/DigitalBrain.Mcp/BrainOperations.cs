using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Mcp;

// The client. Four operations; the MCP tools are thin wrappers over these.
public sealed class BrainOperations(IGrainFactory grains)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public async Task<FireResult> FireAsync(string session, FireRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var from = Parse(session, nameof(session));
        var signal = Signal.Create(request.Type, request.Body);
        NeuronId? to = request.To is null ? null : Parse(request.To, nameof(request));
        CorrelationId? correlation = request.Correlation is null ? null : new CorrelationId(Guid.Parse(request.Correlation));

        var delivered = await Neuron(from).Fire(signal, to, correlation, cancellationToken).ConfigureAwait(false);
        var outgoing = await Query(from).ReadJournal(JournalKind.Outgoing, 0).ConfigureAwait(false);
        var envelope = outgoing.Delta[^1];
        return new(envelope.SignalId.ToString(), envelope.CorrelationId.ToString(), delivered);
    }

    public Task ConnectAsync(ConnectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Signal.Create(request.Type, "{}");
        return Neuron(Parse(request.From, nameof(request))).Connect(Parse(request.To, nameof(request)), request.Type);
    }

    public Task DisconnectAsync(ConnectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Neuron(Parse(request.From, nameof(request))).Disconnect(Parse(request.To, nameof(request)), request.Type);
    }

    public async Task<ReadResult> ReadAsync(ReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Parse(request.Neuron, nameof(request));
        var query = Query(id);
        var what = request.What?.Trim().ToLowerInvariant();
        if (what is not (null or "" or "state" or "synapses" or "incoming" or "outgoing"))
        {
            throw new ArgumentException($"'{request.What}' is not a view. Use state, synapses, incoming or outgoing, or omit it for all four.", nameof(request));
        }

        var all = string.IsNullOrEmpty(what);
        IReadOnlyList<StateEntry>? state = null;
        IReadOnlyList<SynapseEntry>? synapses = null;
        JournalView? incoming = null;
        JournalView? outgoing = null;

        if (all || what == "state")
        {
            state = [.. (await query.ReadState().ConfigureAwait(false)).Select(d => new StateEntry(d.Signal.Type, d.Signal.Body, Name(d.Source), d.Timestamp))];
        }

        if (all || what == "synapses")
        {
            synapses = [.. (await query.ReadSynapses().ConfigureAwait(false)).Select(s => new SynapseEntry(Name(s.Source), Name(s.Target), s.SignalType))];
        }

        if (all || what == "incoming")
        {
            incoming = await ReadJournalAsync(query, JournalKind.Incoming, request.After, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        }

        if (all || what == "outgoing")
        {
            outgoing = await ReadJournalAsync(query, JournalKind.Outgoing, request.After, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        }

        return new(Name(id), state, synapses, incoming, outgoing);
    }

    private static async Task<JournalView> ReadJournalAsync(INeuronQuery query, JournalKind kind, long after, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, timeoutSeconds));
        while (true)
        {
            var read = await query.ReadJournal(kind, after).ConfigureAwait(false);
            if (read.Delta.Count > 0 || timeoutSeconds <= 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return new(read.ResumeSequence, [.. read.Delta.Select((d, index) => Entry(read, d, index))], await TotalAsync(query, kind, read).ConfigureAwait(false));
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // The window position, not the source's own outgoing sequence that rides in the envelope.
    private static JournalEntryView Entry(JournalRead read, SignalDelivery delivery, int index)
        => new(
            read.ResumeSequence - read.Delta.Count + index + 1,
            delivery.Signal.Type,
            delivery.Signal.Body,
            Name(delivery.Source),
            delivery.SignalId.ToString(),
            delivery.CorrelationId.ToString(),
            delivery.Timestamp);

    // A delta-only read carries no snapshot; one read past the tip returns it without a delta.
    private static async Task<long> TotalAsync(INeuronQuery query, JournalKind kind, JournalRead read)
    {
        if (read.ResetSnapshot is { } snapshot)
        {
            return snapshot.TotalRecorded;
        }

        var past = await query.ReadJournal(kind, read.ResumeSequence + 1).ConfigureAwait(false);
        return past.ResetSnapshot?.TotalRecorded ?? read.Delta.Count;
    }

    private INeuron Neuron(NeuronId id) => grains.GetGrain<INeuron>(id.ToGrainId());

    private INeuronQuery Query(NeuronId id) => grains.GetGrain<INeuronQuery>(id.ToGrainId());

    // Plain neurons are named the way callers type them; anything else keeps its "type:name".
    private static string Name(NeuronId id) => id.Type == NeuronId.PlainType ? id.Name : id.ToString();

    private static NeuronId Parse(string text, string parameter)
        => NeuronId.TryParse(text, out var id)
            ? id
            : throw new ArgumentException($"'{text}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", parameter);
}
