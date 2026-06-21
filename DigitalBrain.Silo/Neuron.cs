using DigitalBrain.Protocol;
using Orleans.Journaling;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

[GrainType("digitalbrain.base.v2")]
public abstract class Neuron : DurableGrain, INeuron
{
    protected readonly ILogger Logger;

    protected NeuronId Self => new(this.GetPrimaryKeyString() ?? this.GetGrainId().ToString());

    protected Neuron(ILogger logger)
    {
        Logger = logger;
    }

    // Dual journals for OS-like causality: incoming = received Deliver, outgoing = our Fires.
    protected IDurableList<Synapse> IncomingJournal => this.ServiceProvider.GetRequiredKeyedService<IDurableList<Synapse>>("in-journal");
    protected IDurableList<Synapse> OutgoingJournal => this.ServiceProvider.GetRequiredKeyedService<IDurableList<Synapse>>("out-journal");

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await FireAsync(new NeuronActivated(Self));
    }

    public async ValueTask FireAsync<T>(T payload) where T : Synapse
    {
        var stamped = payload.Stamp(Self);
        OutgoingJournal.Add(stamped);
        await WriteStateAsync();

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
        var snap = OutgoingJournal.Concat(IncomingJournal).DistinctBy(s => new { s.Timestamp, s.Type, Sender = s.Sender?.Value, Receiver = s.Receiver?.Value }).ToList();
        var cp = new Checkpoint(Self, snap.AsReadOnly(), DateTimeOffset.UtcNow);
        await FireAsync(cp);
        return cp;
    }

    public async Task<NeuronId> BranchAsync(Checkpoint checkpoint)
    {
        var branchKey = $"{Self.Value}@branch-{Guid.NewGuid():N}";
        // Use a known concrete (IDemoNeuron) as stand-in receiver for branch replay. It implements INeuron fully.
        var branch = GrainFactory.GetGrain<IDemoNeuron>(branchKey);
        foreach (var s in checkpoint.Snapshot)
        {
            await branch.DeliverAsync(s);
        }
        await branch.FireAsync(new BranchCreated(Self, branchKey));
        return new NeuronId(branchKey);
    }

    // Internal for point to point. Incoming synapses are auto-recorded here (called by sender Fire or direct).
    public async Task DeliverAsync(Synapse synapse)
    {
        IncomingJournal.Add(synapse);
        await WriteStateAsync();

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

    protected virtual Task DispatchSynapse(Synapse synapse) => Task.CompletedTask;
}