using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

    protected IDurableList<Synapse> Journal => this.ServiceProvider.GetRequiredKeyedService<IDurableList<Synapse>>("journal");

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await FireAsync(new NeuronActivated(Self));
    }

    public async ValueTask FireAsync<T>(T payload) where T : Synapse
    {
        var stamped = payload.Stamp(Self);
        Journal.Add(stamped);
        await WriteStateAsync();

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
        Task.FromResult<IReadOnlyList<Synapse>>(Journal.ToList());

    protected async Task ReplayAsync(Func<Synapse, Task> handler, DateTimeOffset? since = null)
    {
        foreach (var s in Journal.Where(x => since == null || x.Timestamp >= since))
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

public sealed class InMemoryDurableList<T> : List<T>, Orleans.Journaling.IDurableList<T>
{
}

public sealed class TestJournaledStateManager : Orleans.Journaling.IJournaledStateManager
{
    // Minimal stub for alpha DurableGrain + IJournaledStateManager in tests/prototype (in-memory simulation).
    // Real deployments should use a storage provider (e.g. Azure) that supplies a full impl.
    public System.Threading.Tasks.ValueTask InitializeAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.ValueTask.CompletedTask;
    public void RegisterState(string stateId, Orleans.Journaling.IJournaledState state) { }
    public bool TryGetState(string stateId, out Orleans.Journaling.IJournaledState? state) { state = null; return false; }
    public System.Threading.Tasks.ValueTask WriteStateAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.ValueTask.CompletedTask;
    public System.Threading.Tasks.ValueTask DeleteStateAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.ValueTask.CompletedTask;
}