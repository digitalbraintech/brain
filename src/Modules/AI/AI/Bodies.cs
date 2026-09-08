using System.Text.Json.Nodes;

namespace DigitalBrain.AI;

/// <summary>An agent's or a chat's configuration, tolerant of every field being absent.</summary>
internal sealed record InstructBody(
    string? Provider,
    string? Model,
    string? System,
    string[] Tools,
    string[] Participants,
    string? Manager,
    int Rounds);

// Signal bodies are JSON, not C# types. These helpers read the shapes documented in
// AIVocabulary and never throw on a missing or mistyped field: a body the module does not
// understand reads as empty rather than as a failed turn.
internal static class Bodies
{
    private static readonly string[] None = [];

    internal static string Text(string body) => String(Parse(body), "text") ?? string.Empty;

    internal static string Write(string text) => new JsonObject { ["text"] = text }.ToJsonString();

    internal static string Said(string author, string text)
        => new JsonObject { ["author"] = author, ["text"] = text }.ToJsonString();

    /// <summary>One transcript line as the model reads it: <c>"{author}: {text}"</c>.</summary>
    internal static string SaidLine(string body)
    {
        var json = Parse(body);
        var text = String(json, "text");
        return string.IsNullOrEmpty(text) ? string.Empty : $"{String(json, "author") ?? "someone"}: {text}";
    }

    internal static InstructBody Instruct(string body)
    {
        var json = Parse(body);
        return new InstructBody(
            String(json, "provider"),
            String(json, "model"),
            String(json, "system"),
            Strings(json, "tools"),
            Strings(json, "participants"),
            String(json, "manager"),
            Int(json, "rounds"));
    }

    private static JsonObject? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonObject? json, string name)
        => json?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int Int(JsonObject? json, string name)
        => json?[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static string[] Strings(JsonObject? json, string name)
        => json?[name] is JsonArray array
            ? [.. array.OfType<JsonValue>()
                .Select(static value => value.TryGetValue<string>(out var text) ? text : null)
                .OfType<string>()]
            : None;
}
