using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.AI;

[Alias(nameof(IAgent))]
public interface IAgent : INeuron, IHandle<AgentRequest>;
