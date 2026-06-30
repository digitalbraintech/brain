namespace DigitalBrain.Core;

// Generic protocol carriers for pack-defined events and LLM intents.
// Name/props let pack code ride the wire as a named event bag without polluting Core with domain types.

[GenerateSerializer]
public record Signal(string Name, IReadOnlyDictionary<string, object?> Props)
    : Synapse(Name, DateTimeOffset.UtcNow);

[GenerateSerializer]
public record AskLlm(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string ReplyType,
    [property: Id(2)] IReadOnlyDictionary<string, object?> ReplyProps,
    // Optional reference to the pack config that selects which LLM provider/key answers this ask.
    // When null the global kernel IChatClient is used. Generic — Core ascribes no Telegram meaning here;
    // only a pack that wants per-scope routing sets these.
    [property: Id(3)] string? ConfigPack = null,
    [property: Id(4)] string? ConfigScope = null)
    : Synapse(nameof(AskLlm), DateTimeOffset.UtcNow);

public interface ILlmResponderNeuron : INeuron, IHandle<AskLlm>
{
    // Well-known singleton key. Broadcasts only reach already-activated grains, so production activates
    // this one instance at startup (kernel Program.cs) to subscribe it to the timeline. Callers that need
    // the responder use this key so the AskLlm -> reply Signal path is reachable cluster-wide.
    const string SingletonKey = "llm-responder-main";
}
