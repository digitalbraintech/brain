namespace DigitalBrain.Core;

// Generic protocol carriers for pack-defined events and LLM intents.
// Name/props let pack code ride the wire as a named event bag without polluting Core with domain types.

[GenerateSerializer]
public record Signal(string Name, IReadOnlyDictionary<string, object?> Props)
    : Synapse(Name, DateTimeOffset.UtcNow);

[GenerateSerializer]
public record AskLlm(string Prompt, string ReplyType, IReadOnlyDictionary<string, object?> ReplyProps)
    : Synapse(nameof(AskLlm), DateTimeOffset.UtcNow);

public interface ILlmResponderNeuron : INeuron, IHandle<AskLlm>
{
    // Well-known singleton key. Broadcasts only reach already-activated grains, so production activates
    // this one instance at startup (kernel Program.cs) to subscribe it to the timeline. Callers that need
    // the responder use this key so the AskLlm -> reply Signal path is reachable cluster-wide.
    const string SingletonKey = "llm-responder-main";
}
