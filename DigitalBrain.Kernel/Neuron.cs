using DigitalBrain.Core;
using Orleans.Journaling;
using Orleans.Streams;
using System.Diagnostics;
using System.Diagnostics.Metrics;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

public sealed class NeuronJournals(
    [FromKeyedServices("in-journal")] IDurableList<Synapse> incoming,
    [FromKeyedServices("out-journal")] IDurableList<Synapse> outgoing)
{
    public IDurableList<Synapse> Incoming { get; } = incoming;
    public IDurableList<Synapse> Outgoing { get; } = outgoing;
}

[GrainType("digitalbrain.base.v2")]
public abstract class Neuron(ILogger logger, NeuronJournals journals) : DurableGrain, INeuron, IAsyncObserver<Synapse>
{
    protected readonly ILogger Logger = logger;
    private IDurableList<Synapse>? _incomingSynapses = journals.Incoming;
    private IDurableList<Synapse>? _outgoingSynapses = journals.Outgoing;
    private StreamSubscriptionHandle<Synapse>? _timelineSubscription;

    // The synapse currently being handled. Synapses fired while handling it are caused by it.
    // Grains are non-reentrant by Orleans contract, so plain field + finally-restore correctly nests causal chains.
    private Synapse? _currentCause;

    // The synapse currently being handled (the cause of anything fired while handling it), exposed so
    // subclasses doing manual point-to-point delivery can preserve causal lineage on stamped synapses.
    protected Synapse? CurrentCause => _currentCause;

    protected NeuronId Self => new(this.GetPrimaryKeyString() ?? this.GetGrainId().ToString());

    // Thin shared reply/context helper (item 13 continuation). IChannelNeuron impls (TelegramChatNeuron + FlutterUiNeuron)
    // use this to propagate causation/reply context via existing Stamp + CorrelationId/CausationId patterns.
    // Centralizes without duplication for cross-channel flows (e.g. Telegram viz -> chart UiSurface -> flutter).
    protected Synapse StampCurrent(Synapse s) => s.Stamp(Self, CurrentCause);

    // Dual journals (self-explanatory names): incoming received via Deliver, outgoing from our Fire calls.
    protected IDurableList<Synapse> IncomingJournal => _incomingSynapses ??= ResolveRequiredJournal("in-journal");
    protected IDurableList<Synapse> OutgoingJournal => _outgoingSynapses ??= ResolveRequiredJournal("out-journal");

    private IDurableList<Synapse> ResolveRequiredJournal(string key)
    {
        var journal = this.ServiceProvider.GetKeyedService<IDurableList<Synapse>>(key);
        if (journal is not null)
        {
            return journal;
        }

        // Fail fast: missing registration means wiring error (prototype or real Azure journal). No silent in-memory degradation.
        throw new InvalidOperationException($"Required journal '{key}' not registered for {Self}. Ensure ConfigurePrototypeJournals() or AddAzureBlobJournalStorage + UseJsonJournalFormat is called on the silo builder.");
    }

    private void AddToJournal(ref IDurableList<Synapse>? journalField, string key, Synapse synapse)
    {
        var target = journalField ??= ResolveRequiredJournal(key);
        try
        {
            target.Add(synapse);
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            // Fail fast instead of silent switch. Durability is required for causation, checkpoints, marketplace etc.
            Logger.LogError(ex, "Journal writer not initialized for durable write of {Key} in {Neuron}.", key, Self);
            throw new InvalidOperationException($"Journal writer for '{key}' is not initialized for {Self}. Operation aborted to preserve durability guarantees.", ex);
        }
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _incomingSynapses ??= ResolveRequiredJournal("in-journal");
        _outgoingSynapses ??= ResolveRequiredJournal("out-journal");

        try
        {
            await base.OnActivateAsync(ct);
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogError(ex, "Journal state writer not initialized on activation for {Neuron}. Durability required.", Self);
            throw new InvalidOperationException($"Journal not ready on activation for {Self}.", ex);
        }

        try
        {
            await FireAsync(new NeuronActivated(Self));
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogWarning(ex, "Activation marker was not journaled for {Neuron}; continuing so the first real synapse can initialize the journal.", Self);
        }

        await SubscribeTimelineIfNeeded();
    }

    // A neuron subscribes to the broadcast timeline iff it has a way to react to broadcasts. The default rule
    // is "declares at least one IHandle<T>"; dynamic hosts (GeneratedNeuron, whose handled types come from an
    // embodied pack's manifest, not static interfaces) override this to subscribe unconditionally.
    protected virtual bool ShouldSubscribeToTimeline => SynapseDispatch.HandledTypes(GetType()).Count > 0;

    // Subscribe to the broadcast timeline when ShouldSubscribeToTimeline says so, so point-to-point-only
    // neurons are unaffected. Explicit subscriptions survive deactivation (Orleans streaming contract), so
    // a reactivated neuron resumes via GetAllSubscriptionHandles + ResumeAsync rather than re-subscribing
    // (avoids duplicate deliveries). Silos that don't register the timeline provider (minimal/legacy test
    // hosts) degrade gracefully: the neuron activates without broadcast reception instead of failing.
    private async Task SubscribeTimelineIfNeeded()
    {
        if (!ShouldSubscribeToTimeline)
            return;

        IAsyncStream<Synapse> stream;
        try
        {
            stream = this.GetStreamProvider(SynapseStream.ProviderName).Timeline();
        }
        catch (KeyNotFoundException)
        {
            Logger.LogDebug("Timeline provider '{Provider}' not registered for {Neuron}; broadcast reception disabled.", SynapseStream.ProviderName, Self);
            return;
        }

        var existing = await stream.GetAllSubscriptionHandles();
        if (existing.Count == 0)
        {
            _timelineSubscription = await stream.SubscribeAsync(this);
            return;
        }

        _timelineSubscription = await existing[0].ResumeAsync(this);
        for (var i = 1; i < existing.Count; i++)
            await existing[i].UnsubscribeAsync();
    }

    // Default broadcast reception: dispatch only synapse types this neuron statically declares IHandle<T> for.
    // Dynamic hosts override to filter through their own runtime manifest instead.
    public virtual Task OnNextAsync(Synapse item, StreamSequenceToken? token = null) =>
        SynapseDispatch.HandledTypes(GetType()).Contains(item.GetType())
            ? SynapseDispatch.DispatchAsync(this, Logger, Self, item)
            : Task.CompletedTask;

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        Logger.LogError(ex, "Timeline stream error in {Neuron}", Self);
        return Task.CompletedTask;
    }

    public async ValueTask FireAsync<T>(T payload) where T : Synapse
    {
        var stamped = payload.Stamp(Self, _currentCause);
        AddToJournal(ref _outgoingSynapses, "out-journal", stamped);
        await WriteJournalStateAsync();

        if (stamped.IsBroadcast)
        {
            await this.GetStreamProvider(SynapseStream.ProviderName).Timeline().OnNextAsync(stamped);
        }
        else if (stamped.Receiver is not null)
        {
            var target = GrainFactory.GetGrain<INeuron>(stamped.Receiver.Value);
            await target.DeliverAsync(stamped);
        }
        else
        {
            await DeliverAsync(stamped);
        }

        NeuronInstrumentation.SynapsesOut.Add(1);
        Logger.LogInformation("Fired {Type} from {Self}", typeof(T).Name, Self);
    }

    protected Task Broadcast(Synapse s) => FireAsync(s with { IsBroadcast = true }).AsTask();

    public Task<IReadOnlyList<Synapse>> GetTimelineAsync() =>
        Task.FromResult<IReadOnlyList<Synapse>>(OutgoingJournal.ToList());

    public Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync() =>
        Task.FromResult<IReadOnlyList<Synapse>>(IncomingJournal.ToList());

    public Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync() =>
        Task.FromResult<IReadOnlyList<Synapse>>(OutgoingJournal.ToList());

    public Task<IReadOnlyList<Synapse>> GetCausalLineageAsync(string correlationId) =>
        Task.FromResult<IReadOnlyList<Synapse>>(OutgoingJournal
            .Where(s => s.CorrelationId == correlationId || s.SynapseId == correlationId)
            .Concat(IncomingJournal.Where(s => s.CorrelationId == correlationId || s.SynapseId == correlationId))
            .OrderBy(s => s.Timestamp)
            .DistinctBy(s => s.SynapseId)
            .ToList());

    public Task<IReadOnlyList<Synapse>> GetTimelineForCorrelationAsync(string correlationId) =>
        GetCausalLineageAsync(correlationId);

    public async ValueTask<Checkpoint> CreateCheckpointAsync()
    {
        // Dedup by the stable SynapseId (a synapse fired then self-delivered appears in both journals as the
        // same instance) — robust vs. the old {Timestamp,Type,Sender,Receiver} heuristic.
        var snap = OutgoingJournal.Concat(IncomingJournal).DistinctBy(s => s.SynapseId).ToList();
        var cp = new Checkpoint(Self, snap.AsReadOnly(), DateTimeOffset.UtcNow);
        await FireAsync(cp);
        return cp;
    }

    public async Task<NeuronId> BranchAsync(Checkpoint checkpoint)
    {
        var branchKey = $"{Self.Value}@branch-{Guid.NewGuid():N}";
        // Branch into a NEW grain of the SAME concrete type as this neuron (was hardcoded to IDemoNeuron),
        // so the fork really is a copy of *this* neuron's behavior, replayed from the checkpoint.
        var branch = GrainFactory.GetGrain<INeuron>(GrainId.Create(this.GetGrainId().Type, branchKey));
        foreach (var s in checkpoint.Snapshot)
        {
            await branch.DeliverAsync(s);
        }
        await branch.FireAsync(new BranchCreated(Self, branchKey));
        return new NeuronId(branchKey);
    }

    // Restore: seed this neuron's incoming journal from a checkpoint WITHOUT re-dispatching handlers
    // (state recovery, not re-execution). Branching, by contrast, replays into a fresh grain.
    public async Task RestoreCheckpointAsync(Checkpoint checkpoint)
    {
        foreach (var s in checkpoint.Snapshot)
        {
            AddToJournal(ref _incomingSynapses, "in-journal", s);
        }
        await WriteJournalStateAsync();
    }

    // Internal for point to point. Incoming synapses are auto-recorded here (called by sender Fire or direct).
    public async Task DeliverAsync(Synapse synapse)
    {
        AddToJournal(ref _incomingSynapses, "in-journal", synapse);
        await WriteJournalStateAsync();

        var previousCause = _currentCause;
        _currentCause = synapse;
        try
        {
            var synapseType = synapse.GetType().Name;
            var neuronType = GetType().Name;
            using var activity = NeuronInstrumentation.Source.StartActivity($"{synapseType} \u2192 {neuronType}");
            if (activity is not null)
            {
                activity.SetTag("neuron.id", Self.Value);
                activity.SetTag("synapse.type", synapseType);
            }

            var handleStopwatch = Stopwatch.StartNew();
            if (!await TryHandleViaDeclaredInterfaceAsync(synapse))
            {
                await DispatchSynapse(synapse);
            }
            handleStopwatch.Stop();

            NeuronInstrumentation.HandleDuration.Record(handleStopwatch.Elapsed.TotalMilliseconds);
            NeuronInstrumentation.SynapsesIn.Add(1);
        }
        finally
        {
            _currentCause = previousCause;
        }
    }

    protected virtual Task DispatchSynapse(Synapse synapse) => Task.CompletedTask;

    // Tries to locate and invoke IHandle<T>.HandleAsync via declared interfaces on this grain (prototype path).
    // Concrete grains should prefer listing IHandle<T> so Orleans + source-gen can handle; this remains for flexibility with dynamic synapses.
    // Logs at Debug when used so prototype reliance is observable.
    private async ValueTask<bool> TryHandleViaDeclaredInterfaceAsync(Synapse synapse)
    {
        var grainType = GetType();
        foreach (var iface in grainType.GetInterfaces())
        {
            if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(IHandle<>))
                continue;

            var handledType = iface.GetGenericArguments()[0];
            if (handledType != synapse.GetType() && !handledType.IsAssignableFrom(synapse.GetType()))
                continue;

            var handleMethod = iface.GetMethod("HandleAsync", new[] { handledType });
            if (handleMethod is null)
                continue;

            Logger.LogDebug("Reflection IHandle<> dispatch for synapse {Type} on {GrainType}", synapse.Type, grainType.Name);
            var result = handleMethod.Invoke(this, new object[] { synapse });
            if (result is Task t) await t;
            else if (result is ValueTask vt) await vt;
            return true;
        }
        return false;
    }

    private async Task WriteJournalStateAsync()
    {
        try
        {
            await WriteStateAsync();
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogError(ex, "Journal state writer not initialized for durable WriteStateAsync in {Neuron}.", Self);
            throw new InvalidOperationException($"Durable journal writer not initialized for {Self}.", ex);
        }
    }

    private static bool IsJournalWriterUninitialized(Exception exception) =>
        exception.GetBaseException().Message.Contains("state journal stream writer is not initialized", StringComparison.OrdinalIgnoreCase);

    public static class NeuronInstrumentation
    {
        public static readonly ActivitySource Source = new("DigitalBrain.Neuron");
        public static readonly Meter Meter = new("DigitalBrain.Neuron");
        public static readonly Counter<long> SynapsesIn = Meter.CreateCounter<long>("db.synapses.in");
        public static readonly Counter<long> SynapsesOut = Meter.CreateCounter<long>("db.synapses.out");
        public static readonly Histogram<double> HandleDuration = Meter.CreateHistogram<double>("db.handle.duration");
    }
}

