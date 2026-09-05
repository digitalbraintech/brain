using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

[Alias("usermessages")]
public interface IUserMessages : INeuron, IHandle<UserMessaged>;
