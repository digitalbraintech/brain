namespace DigitalBrain.AI.Contracts;

/// <summary>
/// The AI module's vocabulary: two grain types and five signal words. This project ships no
/// C# signal types — a signal is a type name plus a JSON body, and the shapes below are the
/// documented contract for those bodies.
/// </summary>
public static class AIVocabulary
{
    /// <summary>
    /// Grain type of an agent neuron: a Session neuron with a model attached. Configured by
    /// <see cref="Instruct"/>, answers <see cref="Ask"/> with <see cref="Reply"/> and
    /// <see cref="Turn"/> with <see cref="Said"/>.
    /// </summary>
    public const string AgentType = "agent";

    /// <summary>
    /// Grain type of a group-chat neuron: participants, a transcript (its own incoming
    /// journal filtered by correlation) and a turn policy.
    /// </summary>
    public const string ChatType = "chat";

    /// <summary>
    /// Configuration, held as latest-per-type.
    /// <para>
    /// For an <see cref="AgentType"/> neuron:
    /// <c>{ "provider": "xai|openai|scripted", "model": "grok-4.6", "system": "...", "tools": ["websearch"] }</c>.
    /// Every field is optional: the provider falls back to the configured default provider,
    /// the model to the provider's default model, and <c>tools</c> names native tools on top
    /// of the four brain operations every agent always has.
    /// </para>
    /// <para>
    /// For a <see cref="ChatType"/> neuron:
    /// <c>{ "participants": ["agent:writer","agent:reviewer"], "manager": "roundrobin", "rounds": 2 }</c>.
    /// </para>
    /// </summary>
    public const string Instruct = "Instruct";

    /// <summary>A question: <c>{ "text": "..." }</c>. Answered by exactly one <see cref="Reply"/> with the same correlation.</summary>
    public const string Ask = "Ask";

    /// <summary>An answer: <c>{ "text": "..." }</c>. Fired back at the source of the <see cref="Ask"/>.</summary>
    public const string Reply = "Reply";

    /// <summary>An invitation to speak: <c>{}</c>. Carries no transcript — the participant reads it.</summary>
    public const string Turn = "Turn";

    /// <summary>One transcript line: <c>{ "author": "agent:writer", "text": "..." }</c>.</summary>
    public const string Said = "Said";
}
