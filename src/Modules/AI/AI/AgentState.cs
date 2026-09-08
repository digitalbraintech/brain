namespace DigitalBrain.AI;

/// <summary>
/// An agent's private working memory: one serialized MAF session per correlation, newest
/// last. Bounded, because a Session neuron outlives every conversation it ever held.
/// </summary>
[GenerateSerializer]
[Alias("db.ai.agent-state")]
public sealed record AgentState([property: Id(0)] List<AgentSessionEntry> Sessions)
{
    /// <summary>How many conversations an agent keeps before it forgets the oldest.</summary>
    public const int MaxSessions = 32;

    public static AgentState Empty => new([]);
}

/// <summary>One conversation: the correlation that names it and MAF's own serialized session.</summary>
[GenerateSerializer]
[Alias("db.ai.agent-session")]
public sealed record AgentSessionEntry([property: Id(0)] string Correlation, [property: Id(1)] string SessionJson);
