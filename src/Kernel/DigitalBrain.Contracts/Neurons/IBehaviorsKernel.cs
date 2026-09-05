using DigitalBrain.Abstractions.Identity;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// Host registry for saved per-instance behaviors. Program mutations belong to IBehavior.
[Alias("db.behaviors-kernel")]
public interface IBehaviorsKernel : IGrainWithStringKey
{
    [ReadOnly]
    [AlwaysInterleave]
    Task<IReadOnlyList<NeuronId>> ReadBehaviorIds();

    Task RegisterBehavior(NeuronId behavior);
}
