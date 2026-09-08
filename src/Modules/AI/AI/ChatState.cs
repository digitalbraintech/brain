using System.Text.Json.Nodes;

namespace DigitalBrain.AI;

/// <summary>
/// A chat's private working memory: one entry per conversation in flight, newest last. The
/// transcript is not in here — that is the chat's incoming journal. Bounded, because a chat
/// neuron outlives every conversation it ever ran.
/// </summary>
[GenerateSerializer]
[Alias("db.ai.chat-state")]
public sealed record ChatState([property: Id(0)] List<ChatRun> Runs)
{
    /// <summary>How many conversations a chat tracks before it forgets the oldest.</summary>
    public const int MaxRuns = 16;

    public static ChatState Empty => new([]);
}

/// <summary>
/// One conversation: the correlation that names it, who asked, the turn policy's own state,
/// and the participant whose <c>Said</c> the chat is waiting for.
/// </summary>
[GenerateSerializer]
[Alias("db.ai.chat-run")]
public sealed record ChatRun(
    [property: Id(0)] string Correlation,
    [property: Id(1)] string Asker,
    [property: Id(2)] string StateJson,
    [property: Id(3)] string? PendingParticipant);

/// <summary>
/// The turn state the group-chat manager is rebuilt from: who is in this run, how many rounds
/// it lasts, and how many turns have been spoken. Round-robin selection is a function of the
/// turn count, so replaying <c>Turn</c> selections on a fresh manager restores it exactly.
/// </summary>
internal sealed record RunPolicy(string[] Participants, int Rounds, int Turn)
{
    /// <summary>Every turn of every round: two participants over one round is two turns.</summary>
    internal int TotalTurns => Rounds * Participants.Length;

    internal string ToJson()
        => new JsonObject
        {
            ["participants"] = new JsonArray([.. Participants.Select(static p => (JsonNode)JsonValue.Create(p))]),
            ["rounds"] = Rounds,
            ["turn"] = Turn,
        }.ToJsonString();

    internal static RunPolicy Parse(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("A chat run's turn state is not a JSON object.");
        var participants = node["participants"] as JsonArray ?? [];
        return new RunPolicy(
            [.. participants.OfType<JsonValue>().Select(static v => v.TryGetValue<string>(out var text) ? text : null).OfType<string>()],
            node["rounds"] is JsonValue rounds && rounds.TryGetValue<int>(out var r) ? r : 1,
            node["turn"] is JsonValue turn && turn.TryGetValue<int>(out var t) ? t : 0);
    }
}
