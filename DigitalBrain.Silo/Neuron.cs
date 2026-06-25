using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

public sealed class NeuronJournals(
    [FromKeyedServices("in-journal")] IDurableList<Synapse> incoming,
    [FromKeyedServices("out-journal")] IDurableList<Synapse> outgoing)
{
    public IDurableList<Synapse> Incoming { get; } = incoming;
    public IDurableList<Synapse> Outgoing { get; } = outgoing;
}

[GrainType("digitalbrain.base.v2")]
public abstract class Neuron : DurableGrain, INeuron
{
    protected readonly ILogger Logger;
    private IDurableList<Synapse>? _incomingSynapses;
    private IDurableList<Synapse>? _outgoingSynapses;

    // The synapse currently being handled. Synapses fired while handling it are caused by it.
    // Grains are non-reentrant by Orleans contract, so plain field + finally-restore correctly nests causal chains.
    private Synapse? _currentCause;

    protected NeuronId Self => new(this.GetPrimaryKeyString() ?? this.GetGrainId().ToString());

    protected Neuron(ILogger logger, NeuronJournals journals)
    {
        Logger = logger;
        _incomingSynapses = journals.Incoming;
        _outgoingSynapses = journals.Outgoing;
    }

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
    }

    public async ValueTask FireAsync<T>(T payload) where T : Synapse
    {
        var stamped = payload.Stamp(Self, _currentCause);
        AddToJournal(ref _outgoingSynapses, "out-journal", stamped);
        await WriteJournalStateAsync();

        if (stamped.IsBroadcast)
        {
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

        Logger.LogInformation("Fired {Type} from {Self}", typeof(T).Name, Self);
    }

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

        // Synapses fired while handling this one inherit its correlation and are caused by it.
        var previousCause = _currentCause;
        _currentCause = synapse;
        try
        {
            // Reflection-based IHandle<T> lookup kept for flexibility with dynamic/embodied synapses.
            // Prefer explicit interface impls (e.g. : IHandle<SpecificSynapse>) + Orleans grain dispatch for perf/AOT.
            // Logs (Debug) when hit to surface reliance on prototype path.
            if (!await TryHandleViaDeclaredInterfaceAsync(synapse))
            {
                await DispatchSynapse(synapse);
            }
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
}

