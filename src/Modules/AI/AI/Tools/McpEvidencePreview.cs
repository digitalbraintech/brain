using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DigitalBrain.AI;

public static partial class McpEvidencePreview
{
    public static JsonElement Redact(JsonElement content)
    {
        var source = content.GetRawText();
        var tree = JsonNode.Parse(source);
        Scrub(tree);
        var redacted = tree?.ToJsonString() ?? "null";
        if (tree is JsonObject envelope && !JsonElement.DeepEquals(content, JsonSerializer.SerializeToElement(tree)))
        {
            var meta = envelope["_meta"] as JsonObject ?? new JsonObject();
            envelope["_meta"] = meta;
            var details = meta["digitalbrain"] as JsonObject ?? new JsonObject();
            meta["digitalbrain"] = details;
            details["redacted"] = true;
            redacted = envelope.ToJsonString();
        }

        using var document = JsonDocument.Parse(redacted);
        return document.RootElement.Clone();
    }

    public static string Create(string screenedContent)
    {
        var redacted = ScrubText(screenedContent);
        return redacted.Length > 1800 ? redacted[..1800] + "… [preview truncated]" : redacted;
    }

    private static void Scrub(JsonNode? node)
    {
        if (node is JsonObject map)
        {
            var credentialEntry = map["name"] is JsonValue name && name.TryGetValue<string>(out var label)
                && SecretNamePattern().IsMatch(label);
            foreach (var (key, value) in map.ToArray())
            {
                if (SecretNamePattern().IsMatch(key) || (credentialEntry && key.Equals("value", StringComparison.OrdinalIgnoreCase)))
                {
                    map[key] = "[redacted]";
                }
                else if (value is JsonValue text && text.TryGetValue<string>(out var content))
                {
                    map[key] = ScrubText(content);
                }
                else { Scrub(value); }
            }
        }
        else if (node is JsonArray list)
        {
            for (var index = 0; index < list.Count; index++)
            {
                if (list[index] is JsonValue text && text.TryGetValue<string>(out var content))
                {
                    list[index] = ScrubText(content);
                }
                else { Scrub(list[index]); }
            }
        }
    }

    private static string ScrubText(string value)
    {
        // Some MCP tools return JSON in a text block instead of structuredContent.
        if (value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('['))
        {
            try
            {
                var nested = JsonNode.Parse(value);
                Scrub(nested);
                return JsonNode.DeepEquals(JsonNode.Parse(value), nested) ? value : nested?.ToJsonString() ?? value;
            }
            catch (JsonException) { }
        }

        // MCP tools also return a prose preamble followed by JSON. Redact those
        // documents structurally so null/numeric credentials remain valid JSON,
        // and named environment entries keep their original business shape.
        var result = new StringBuilder();
        var offset = 0;
        foreach (Match start in JsonDocumentStartPattern().Matches(value))
        {
            if (start.Index < offset) { continue; }
            try
            {
                var bytes = Encoding.UTF8.GetBytes(value[start.Index..]);
                var reader = new Utf8JsonReader(bytes);
                using var document = JsonDocument.ParseValue(ref reader);
                var length = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, checked((int)reader.BytesConsumed)));
                var original = value.Substring(start.Index, length);
                var nested = JsonNode.Parse(original);
                Scrub(nested);
                result.Append(ScrubPlainText(value[offset..start.Index]));
                result.Append(JsonNode.DeepEquals(JsonNode.Parse(original), nested) ? original : nested?.ToJsonString());
                offset = start.Index + length;
            }
            catch (JsonException) { }
        }
        result.Append(ScrubPlainText(value[offset..]));
        return result.ToString();
    }

    private static string ScrubPlainText(string value)
    {
        var redacted = UriCredentialPattern().Replace(value, "$1[redacted]@");
        redacted = BearerPattern().Replace(redacted, "$1[redacted]");
        redacted = CredentialPattern().Replace(redacted, "$1[redacted]");
        return QueryPattern().Replace(redacted, "$1[redacted]");
    }

    [GeneratedRegex("(?i)(password|secret|api[_-]*key(?:$|[_-])|(?:^|[_-])(?:access[_-]?token|refresh[_-]?token|token)(?:$|[_-])|authorization|connection[_-]?string)", RegexOptions.None, 1000)]
    private static partial Regex SecretNamePattern();

    [GeneratedRegex("(?i)((?:password|secret|access[_-]?token|refresh[_-]?token|api[_-]?key|authorization|connection[_-]?string)[\\\"'\\s]*[:=][\\\"'\\s]*)([^\\\"'\\s,;}]+)", RegexOptions.None, 1000)]
    private static partial Regex CredentialPattern();

    [GeneratedRegex("(?i)(bearer\\s+)[a-z0-9._~+/-]+=*", RegexOptions.None, 1000)]
    private static partial Regex BearerPattern();

    [GeneratedRegex("([?&](?:t|token|key|code|sig|signature)=)[^&\\\"\\s]+", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex QueryPattern();

    [GeneratedRegex(@"(?i)([a-z][a-z0-9+.-]*://)[^\s/@]+@", RegexOptions.None, 1000)]
    private static partial Regex UriCredentialPattern();

    [GeneratedRegex(@"(?m)^[ \t]*[\[{]", RegexOptions.None, 1000)]
    private static partial Regex JsonDocumentStartPattern();
}
