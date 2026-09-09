using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.AI;

public static class ApplicationAgentExtensions
{
    public static CommandPort<AgentRequest, AgentReply> Agent(
        this ApplicationDefinition application,
        string key,
        Func<AgentRequest, ApplicationExecutionContext, CancellationToken, Task<AgentReply>> handler)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(handler);
        var command = application.Command(key, handler);
        application.Implement<IAgent>(key, key);
        return command;
    }
}
