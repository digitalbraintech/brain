using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.AI;
using DigitalBrain.UI;

namespace DigitalBrain.Chat;

public static class ApplicationChatPorts
{
    public static ComposerEvents Events(this NeuronReference<IComposer> composer) => new(composer);
    public static AssistantInputs Inputs(this NeuronReference<IAssistant> assistant) => new(assistant);
}

public sealed class ComposerEvents
{
    internal ComposerEvents(NeuronReference<IComposer> composer)
        => UserMessaged = new(composer.ApplicationScopeId, "neuron", composer.Id.ToString(), "",
            "user-messaged", "chat.user-messaged/v1");

    public EventPort<UserMessaged> UserMessaged { get; }
}

public sealed class AssistantInputs
{
    internal AssistantInputs(NeuronReference<IAssistant> assistant)
        => UserMessaged = new(assistant.ApplicationScopeId, assistant.Id, "chat.user-messaged/v1");

    public InputPort<UserMessaged> UserMessaged { get; }
}
