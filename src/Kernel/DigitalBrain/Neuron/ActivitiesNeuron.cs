using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

[GrainType(IActivities.GrainTypeName)]
internal sealed class ActivitiesNeuron : Neuron, IActivities
{
    private readonly IDurableDictionary<string, byte[]> _activities;
    private readonly Serializer<ActivityState> _serializer;

    public ActivitiesNeuron(NeuronRuntime runtime) : base(runtime)
    {
        _activities = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>("activities.state.v1");
        _serializer = ServiceProvider.GetRequiredService<Serializer<ActivityState>>();
    }

    public async Task HandleAsync(ActivityExecutionChanged signal, CancellationToken cancellationToken)
    {
        ActivityFacts.RequireOwner(signal, Id.Owner, CurrentDelivery?.Principal);
        var key = $"{signal.Principal?.ToString() ?? "system"}/{signal.CorrelationId}";
        var state = _activities.TryGetValue(key, out var existing) ? _serializer.Deserialize(existing) : new ActivityState();
        if (state.Apply(signal))
        {
            _activities[key] = _serializer.SerializeToArray(state);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
        }
        // An earlier delivery can have committed state before an observer failed.
        // Retrying republishes the same version without applying the fact twice.
        await BroadcastAsync(new ActivityChanged(state.View())).ConfigureAwait(true);
    }

    public Task HandleAsync(ReadActivities signal, CancellationToken cancellationToken)
    {
        var principal = VerifiedActor.Current?.PrincipalId;
        var views = _activities.Select(pair => _serializer.Deserialize(pair.Value).View())
            .Where(view => view.Principal is null || view.Principal == principal)
            .OrderByDescending(view => view.UpdatedAt).Take(Math.Clamp(signal.Limit, 1, 500)).ToArray();
        return ReplyAsync(new ActivitiesSnapshot(TimeProvider.GetUtcNow(), views));
    }
}

internal static class ActivityFacts
{
    internal static void RequireOwner(ActivityExecutionChanged fact, OwnerId owner, PrincipalId? principal)
    {
        if (fact.Source.Owner != owner || fact.Target is { } target && target.Owner != owner
            || fact.Principal != principal)
        {
            throw new NeuronAuthorizationException("Activity facts must retain their owner and verified principal.");
        }
        if (fact.Phase is not ("running" or "waiting" or "completed" or "failed" or "cancelled" or "observed"))
        {
            throw new ArgumentException("Unknown activity execution phase.", nameof(fact));
        }
    }
}
