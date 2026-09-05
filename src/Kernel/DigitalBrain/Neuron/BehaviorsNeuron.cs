using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;
[GrainType("behaviors")]
internal sealed class BehaviorsNeuron : Neuron, IBehaviors, IBehaviorsKernel
{
    private readonly IDurableDictionary<string, NeuronId> _behaviorIds;

    public BehaviorsNeuron(NeuronRuntime runtime) : base(runtime)
    {
        _behaviorIds = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, NeuronId>>("behaviors.index");
    }
    public Task<IReadOnlyList<NeuronId>> ReadBehaviorIds() => Task.FromResult<IReadOnlyList<NeuronId>>([.._behaviorIds.Select(pair => pair.Value)]);
    public async Task RegisterBehavior(NeuronId behavior)
    {
        var principal = VerifiedActor.Current?.PrincipalId ?? throw new NeuronAuthorizationException("Behavior registration requires an authenticated principal.");
        if (behavior.Owner != Id.Owner || behavior.Type != "behavior" || !PrincipalPartition.OwnsInstance(principal, behavior.Name))
        {
            throw new NeuronAuthorizationException("This behavior belongs to another principal.");
        }

        _behaviorIds[behavior.Name] = behavior;
        await RecordOutgoingAsync(new BehaviorWorkAvailable(behavior)).ConfigureAwait(true);
    }
}
