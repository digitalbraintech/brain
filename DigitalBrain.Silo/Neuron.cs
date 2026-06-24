using DigitalBrain.Protocol;
using Orleans.Journaling;
using Orleans.Runtime;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

[GrainType("digitalbrain.base.v2")]
public abstract class Neuron : DurableGrain, INeuron
{
    protected readonly ILogger Logger;
    private IDurableList<Synapse>? _incomingJournal;
    private IDurableList<Synapse>? _outgoingJournal;

    // The synapse currently being handled. Synapses fired while handling it are caused by it.
    // Grains are non-reentrant, so a plain field with save/restore correctly nests self-fire chains.
    private Synapse? _cause;

    protected NeuronId Self => new(this.GetPrimaryKeyString() ?? this.GetGrainId().ToString());

    protected Neuron(ILogger logger)
    {
        Logger = logger;
    }

    // Dual journals for OS-like causality: incoming = received Deliver, outgoing = our Fires.
    protected IDurableList<Synapse> IncomingJournal => _incomingJournal ??= ResolveJournal("in-journal");
    protected IDurableList<Synapse> OutgoingJournal => _outgoingJournal ??= ResolveJournal("out-journal");

    private IDurableList<Synapse> ResolveJournal(string key)
    {
        var journal = this.ServiceProvider.GetKeyedService<IDurableList<Synapse>>(key);
        if (journal is not null)
        {
            return journal;
        }

        Logger.LogWarning("Journal service {JournalKey} is not registered; using prototype in-memory journal for {Neuron}.", key, Self);
        return new InMemoryJournalForPrototype<Synapse>();
    }

    private void AddToJournal(ref IDurableList<Synapse>? journal, string key, Synapse synapse)
    {
        var target = journal ??= ResolveJournal(key);
        try
        {
            target.Add(synapse);
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogWarning(ex, "Journal service {JournalKey} cannot write for {Neuron}; switching to prototype in-memory journal.", key, Self);
            target = new InMemoryJournalForPrototype<Synapse>();
            journal = target;
            target.Add(synapse);
        }
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        try
        {
            await base.OnActivateAsync(ct);
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogWarning(ex, "Journal state writer is not initialized during activation for {Neuron}; continuing with in-memory journal state.", Self);
        }

        await FireAsync(new NeuronActivated(Self));
    }

    public async ValueTask FireAsync<T>(T payload) where T : Synapse
    {
        var stamped = payload.Stamp(Self, _cause);
        AddToJournal(ref _outgoingJournal, "out-journal", stamped);
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
            AddToJournal(ref _incomingJournal, "in-journal", s);
        }
        await WriteJournalStateAsync();
    }

    // Internal for point to point. Incoming synapses are auto-recorded here (called by sender Fire or direct).
    public async Task DeliverAsync(Synapse synapse)
    {
        AddToJournal(ref _incomingJournal, "in-journal", synapse);
        await WriteJournalStateAsync();

        // Synapses fired while handling this one inherit its correlation and are caused by it.
        var previousCause = _cause;
        _cause = synapse;
        try
        {
            // Support IHandle<T> by reflection for any implementing neuron (prototype; source-gen later)
            var handled = false;
            var grainType = GetType();
            foreach (var iface in grainType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IHandle<>))
                {
                    var handledType = iface.GetGenericArguments()[0];
                    if (handledType == synapse.GetType() || handledType.IsAssignableFrom(synapse.GetType()))
                    {
                        var method = iface.GetMethod("HandleAsync", new[] { handledType });
                        if (method != null)
                        {
                            var result = method.Invoke(this, new object[] { synapse });
                            if (result is Task t) await t;
                            else if (result is ValueTask vt) await vt;
                            handled = true;
                            break;
                        }
                    }
                }
            }

            if (!handled)
                await DispatchSynapse(synapse);
        }
        finally
        {
            _cause = previousCause;
        }
    }

    protected virtual Task DispatchSynapse(Synapse synapse) => Task.CompletedTask;

    private async Task WriteJournalStateAsync()
    {
        try
        {
            await WriteStateAsync();
        }
        catch (Exception ex) when (IsJournalWriterUninitialized(ex))
        {
            Logger.LogWarning(ex, "Journal state writer is not initialized for {Neuron}; continuing with in-memory journal state.", Self);
        }
    }

    private static bool IsJournalWriterUninitialized(Exception exception) =>
        exception.GetBaseException().Message.Contains("state journal stream writer is not initialized", StringComparison.OrdinalIgnoreCase);
}
