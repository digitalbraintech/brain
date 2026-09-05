using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

[Alias("db.behavior")]
public interface IBehavior : INeuron,
    IHandle<SaveBehaviorScript>, IHandle<ReadBehavior>, IHandle<EnableBehavior>,
    IHandle<DisableBehavior>, IHandle<InvokeBehavior>;
