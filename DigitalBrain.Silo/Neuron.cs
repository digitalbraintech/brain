using DigitalBrain.Protocol;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace DigitalBrain.Silo;

[GrainType("neuro.base.v2")]
public abstract class Neuron : Grain, INeuron
{
    protected readonly ILogger Logger;
    protected readonly IPersistentState<List<Synapse>> Journal;

    protected NeuronId Self => new(this.GetPrimaryKeyString() ?? this.GetGrainId().ToString());

    protected Neuron(ILogger logger, [PersistentState("journal", "Default")] IPersistentState<List<Synapse>> journal)
    {
        Logger = logger;
        Journal = journal;
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await Journal.ReadStateAsync();
        await FireAsync(new NeuronActivated(Self));
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        await Journal.WriteStateAsync();
        await base.OnDeactivateAsync(reason, ct);
    }

    public async ValueTask FireAsync<T>(T payload) where T : Synapse
    {
        var stamped = payload.Stamp(Self);
        Journal.State.Add(stamped);
        await Journal.WriteStateAsync();

        if (stamped.IsBroadcast)
        {
            // Broadcast via streams deferred for initial prototype (requires more stream config)
        }
        else if (stamped.Receiver is not null)
        {
            var target = GrainFactory.GetGrain<INeuron>(stamped.Receiver.Value);
            await target.DeliverAsync(stamped);
        }
        else
        {
            // Command/event fired directly on this neuron -> local dispatch (for IHandle etc)
            await DeliverAsync(stamped);
        }

        Logger.LogInformation("Fired {Type} from {Self}", typeof(T).Name, Self);
    }

    public Task<IReadOnlyList<Synapse>> GetTimelineAsync() =>
        Task.FromResult<IReadOnlyList<Synapse>>(Journal.State);

    protected async Task ReplayAsync(Func<Synapse, Task> handler, DateTimeOffset? since = null)
    {
        foreach (var s in Journal.State.Where(x => since == null || x.Timestamp >= since))
            await handler(s);
    }

    // Internal for point to point
    public async Task DeliverAsync(Synapse synapse)
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

    protected virtual Task DispatchSynapse(Synapse synapse) => Task.CompletedTask;
}