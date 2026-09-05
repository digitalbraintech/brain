using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;

namespace DigitalBrain.AI;

[Alias("DigitalBrain.AI.IAssistant")]
public interface IAssistant : IAgent, IHandle<UserMessaged>;

