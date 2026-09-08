using System.Text.Json;

namespace DigitalBrain.AI;

internal static class SignalText
{
    public static string Read(string body)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString() ?? ""
            : "";
    }

    public static string Write(string text)
        => JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = text });

    public static string Said(string author, string text)
        => JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["author"] = author,
            ["text"] = text,
        });
}
