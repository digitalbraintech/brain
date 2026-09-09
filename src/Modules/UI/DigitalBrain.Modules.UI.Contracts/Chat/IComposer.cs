using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

[Alias("usermessages")]
public interface IComposer : INeuron, IHandle<UserMessaged>, IHandle<Responded>, IHandle<TurnLifecycle>
{
    const string DefaultInstanceName = "inbox";
    const string GrainTypeName = "usermessages";
}
