using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Interactions;

namespace DigitalBrain.AI;

internal static class AgentTelemetry
{
    internal const string SourceName = "DigitalBrain.AI.Agents";
    private static readonly ActivitySource Source = new(SourceName);

    internal static Activity? Start(NeuronId agent, string name, bool enableSensitiveData)
    {
        var turn = AgentTurnContext.Current;
        var activity = Source.StartActivity($"invoke_agent {name}", ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.provider.name", "digitalbrain");
        activity?.SetTag("gen_ai.agent.id", agent.ToString());
        activity?.SetTag("gen_ai.agent.name", name);
        activity?.SetTag("gen_ai.conversation.id", (turn?.Chat ?? agent).ToString());
        activity?.SetTag("db.command.id", turn?.CommandId.ToString());
        // MEAI 10.9 consults its nearest invoke_agent span for this opt-in when
        // recording function arguments/results. This is not an exported tag.
        activity?.SetCustomProperty("__EnableSensitiveData__", enableSensitiveData ? "true" : "false");
        // Message/tool content is captured once by the configured MEAI pipeline.
        return activity;
    }
}
