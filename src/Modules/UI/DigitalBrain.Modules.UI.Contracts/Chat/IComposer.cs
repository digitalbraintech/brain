using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

[Alias("usermessages")]
public interface IComposer : INeuron, IHandle<UserMessaged>
{
    const string DefaultInstanceName = "inbox";
    const string GrainTypeName = "usermessages";
}

[Obsolete("Use IComposer.")]
public interface IUserMessages : IComposer;
