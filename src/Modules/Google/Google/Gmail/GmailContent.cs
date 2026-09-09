using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Sdk;

namespace DigitalBrain.Google;

internal static class GmailContent
{
    // Native MCP arguments arrive as JSON values. Preserve their names/schema while applying
    // the same bounds and safe defaults formerly enforced by the hand-written adapter.
    internal static Dictionary<string, object?> Normalize(string tool, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = arguments.ToDictionary(pair => pair.Key, pair => pair.Value is JsonElement json
            ? json.ValueKind switch
            {
                JsonValueKind.String => json.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when json.TryGetInt32(out var number) => number,
                JsonValueKind.Array when json.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
                    => json.EnumerateArray().Select(item => item.GetString()!).ToArray(),
                JsonValueKind.Null => null,
                _ => json,
            } : pair.Value, StringComparer.Ordinal);
        if (tool == "search_threads")
        {
            result.TryAdd("pageSize", 10);
            result.TryAdd("includeTrash", false);
            result.TryAdd("view", "THREAD_VIEW_MINIMAL");
        }
        if (tool == "get_thread")
        {
            result.TryAdd("messageFormat", "MINIMAL");
        }
        if (tool == "create_draft")
        {
            result.TryAdd("to", System.Array.Empty<string>());
            result.TryAdd("cc", System.Array.Empty<string>());
            result.TryAdd("bcc", System.Array.Empty<string>());
        }
        ValidateArguments(tool, result);
        return result;
    }

    internal static void ValidateArguments(string tool, IReadOnlyDictionary<string, object?> args)
    {
        var allowed = tool switch
        {
            "search_threads" => new[] { "query", "pageSize", "pageToken", "includeTrash", "view" },
            "get_thread" => ["threadId", "messageFormat"],
            "list_labels" => [],
            "create_draft" => ["to", "cc", "bcc", "subject", "body"],
            _ => throw new McpOperationException("This Gmail operation is not allowed."),
        };
        if (args.Keys.Any(k => !allowed.Contains(k, StringComparer.Ordinal)))
        {
            throw new McpOperationException("Unsupported Gmail arguments.");
        }

        if (tool == "search_threads")
        {
            Text(args, "query", 2048); Text(args, "pageToken", 2048, optional: true);
            if (!args.TryGetValue("pageSize", out var size) || size is not int count || count is < 1 or > 10)
            {
                throw new McpOperationException("Gmail pageSize must be between 1 and 10.");
            }

            if (!args.TryGetValue("view", out var view) || view is not "THREAD_VIEW_MINIMAL")
            {
                throw new McpOperationException("Unsupported Gmail thread view.");
            }

            if (!args.TryGetValue("includeTrash", out var trash) || trash is not bool)
            {
                throw new McpOperationException("includeTrash must be a boolean.");
            }
        }
        else if (tool == "get_thread")
        {
            Text(args, "threadId", 256, nonempty: true);
            if (!args.TryGetValue("messageFormat", out var format) || format is not ("MINIMAL" or "PLAIN_TEXT"))
            {
                throw new McpOperationException("Only MINIMAL or PLAIN_TEXT Gmail content is allowed.");
            }
        }
        else if (tool == "create_draft")
        {
            var recipients = new[] { "to", "cc", "bcc" }.SelectMany(k => args.TryGetValue(k, out var value) && value is string[] emails
                ? emails : throw new McpOperationException("Draft recipients must be plain email arrays.")).ToArray();
            if (recipients.Length is < 1 or > 20 || recipients.Any(e => e.Length > 320 || e.Any(char.IsWhiteSpace)
                || !MailAddress.TryCreate(e, out var address) || address.Address != e || address.DisplayName.Length != 0))
            {
                throw new McpOperationException("Supply 1–20 plain email addresses without display names.");
            }

            Text(args, "subject", 998); Text(args, "body", 12000);
            if (((string)args["subject"]!).Any(c => c is '\r' or '\n'))
            {
                throw new McpOperationException("A draft subject must be a single line.");
            }
        }
        if (JsonSerializer.SerializeToUtf8Bytes(args).Length > 30000)
        {
            throw new McpOperationException("Gmail arguments exceed the 30 KiB input limit.");
        }
    }

    private static void Text(IReadOnlyDictionary<string, object?> args, string key, int max, bool optional = false, bool nonempty = false)
    {
        if (optional && (!args.TryGetValue(key, out var optionalValue) || optionalValue is null))
        {
            return;
        }

        if (!args.TryGetValue(key, out var value) || value is not string text || text.Length > max || nonempty && string.IsNullOrWhiteSpace(text))
        {
            throw new McpOperationException($"Invalid Gmail {key}; maximum length is {max}.");
        }
    }

    // Positive projection only: raw, HTML, attachments, headers and unknown fields never cross this boundary.
    internal static JsonElement Project(string tool, JsonElement root, IReadOnlyDictionary<string, object?> args)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new McpOperationException("Gmail MCP returned an invalid response shape.");
        }

        var result = new JsonObject { ["untrustedData"] = true, ["truncated"] = false };
        var truncated = false;
        if (tool == "create_draft")
        {
            foreach (var field in new[] { "id", "messageId", "threadId" })
            {
                CopyText(root, result, field, 256, ref truncated);
            }

            if (result["id"] is null)
            {
                throw new McpOperationException("Gmail did not return a draft id. Check Drafts before trying again.");
            }
        }
        else if (tool == "list_labels")
        {
            var labels = Array(root, "labels");
            var projected = new JsonArray();
            foreach (var label in labels.Take(100))
            {
                var item = new JsonObject(); CopyText(label, item, "labelId", 256, ref truncated); CopyText(label, item, "name", 512, ref truncated); projected.Add(item);
            }
            truncated |= labels.Length > 100; result["labels"] = projected;
        }
        else if (tool == "search_threads")
        {
            var threads = Array(root, "threads"); var projected = new JsonArray(); var remainingMessages = 10;
            foreach (var thread in threads.Take((int)args["pageSize"]!))
            {
                projected.Add(Thread(thread, false, ref remainingMessages, ref truncated));
            }

            truncated |= threads.Length > (int)args["pageSize"]!; result["threads"] = projected;
            CopyText(root, result, "nextPageToken", 2048, ref truncated);
            CopyText(root, result, "resultCountEstimate", 32, ref truncated);
        }
        else
        {
            var remainingMessages = 10;
            var thread = Thread(root, (string)args["messageFormat"]! == "PLAIN_TEXT", ref remainingMessages, ref truncated);
            foreach (var p in thread.ToArray()) { thread.Remove(p.Key); result[p.Key] = p.Value; }
        }
        result["truncated"] = truncated;
        // Enforce UTF-8 JSON size, not just character counts. Drop complete trailing items or
        // shorten plaintext bodies, retaining explicit markers rather than invalid JSON.
        while (JsonSerializer.SerializeToUtf8Bytes(result).Length > 32768)
        {
            result["truncated"] = true;
            if (!Shrink(result))
            {
                throw new McpOperationException("Gmail response exceeds the 32 KiB output limit. Narrow the request.");
            }
        }
        return JsonSerializer.SerializeToElement(result);
    }
    private static JsonObject Thread(JsonElement root, bool bodies, ref int remaining, ref bool truncated)
    {
        var result = new JsonObject(); CopyText(root, result, "id", 256, ref truncated);
        var messages = Array(root, "messages"); var projected = new JsonArray();
        foreach (var message in messages.Take(remaining))
        {
            var item = new JsonObject();
            foreach (var field in new[] { "id", "subject", "snippet", "sender", "date" })
            {
                CopyText(message, item, field, field == "snippet" ? 1500 : field == "subject" ? 998 : 320, ref truncated);
            }

            foreach (var field in new[] { "toRecipients", "ccRecipients", "labelIds" })
            {
                if (!message.TryGetProperty(field, out var values))
                {
                    continue;
                }

                if (values.ValueKind != JsonValueKind.Array)
                {
                    throw new McpOperationException("Gmail returned invalid message metadata.");
                }

                var items = new JsonArray();
                foreach (var value in values.EnumerateArray().Take(20))
                {
                    items.Add(Limit(value.GetString() ?? "", 320, ref truncated));
                }

                truncated |= values.GetArrayLength() > 20; item[field] = items;
            }
            if (bodies)
            {
                CopyText(message, item, "plaintextBody", 12000, ref truncated);
            }

            projected.Add(item);
        }
        truncated |= messages.Length > remaining; remaining -= projected.Count;
        result["messages"] = projected; return result;
    }
    private static JsonElement[] Array(JsonElement root, string name)
        => !root.TryGetProperty(name, out var array) ? [] : array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToArray() : throw new McpOperationException("Gmail MCP returned an invalid collection.");
    private static void CopyText(JsonElement root, JsonObject result, string key, int max, ref bool truncated)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new McpOperationException("Gmail MCP returned invalid text metadata.");
        }

        result[key] = Limit(value.GetString()!, max, ref truncated);
    }
    private static string Limit(string value, int max, ref bool truncated)
    {
        if (value.Length <= max)
        {
            return value;
        }

        truncated = true; return value[..(max - 14)] + " …[truncated]";
    }
    private static bool Shrink(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["plaintextBody"] is JsonValue body && body.TryGetValue<string>(out var text) && text.Length > 512)
            { obj["plaintextBody"] = text[..(text.Length / 2)] + " …[truncated]"; return true; }
            foreach (var p in obj)
            {
                if (p.Value is not null && Shrink(p.Value))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null && Shrink(child))
                {
                    return true;
                }
            }

            if (array.Count > 1) { array.RemoveAt(array.Count - 1); return true; }
        }
        return false;
    }
}
