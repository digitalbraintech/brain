using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Deterministic, offline stand-in for a real LLM (AITestingClients). Scripts exactly two
// tool-calling scenarios the kit UC1/UC2 E2E suite depends on -- render_chart and
// generate_image -- keyed off the latest user message; everything else keeps the original
// fixed reply so every other testing-mode caller (MCP, BDD, simulation) is unaffected.
internal sealed partial class TestChatClient : IChatClient
{
    private const string Reply = "Test assistant reply.";
    private const string RenderedReply = "Rendered.";
    private const string GeneratedReply = "Generated.";
    private const string RenderChartToolName = "render_chart";
    private const string ShowSpreadsheetToolName = "show_spreadsheet";
    private const string GenerateImageToolName = "generate_image";
    private const string FallbackChatName = "main";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        var conversation = messages.ToList();

        // A FunctionResultContent in the history means FunctionInvokingChatClient already ran
        // the tool this client asked for on the prior round; reply per whichever tool that was
        // (its FunctionCallContent is still in the same history, appended by that middleware).
        if (conversation.Any(static m => m.Contents.OfType<FunctionResultContent>().Any()))
        {
            var priorCall = conversation
                .SelectMany(static m => m.Contents.OfType<FunctionCallContent>())
                .FirstOrDefault();
            var reply = string.Equals(priorCall?.Name, GenerateImageToolName, StringComparison.Ordinal)
                ? GeneratedReply
                : RenderedReply;
            yield return new ChatResponseUpdate(ChatRole.Assistant, reply) { FinishReason = ChatFinishReason.Stop };
            yield break;
        }

        var lastUser = conversation.LastOrDefault(static m => m.Role == ChatRole.User)?.Text ?? "";
        var tools = options?.Tools?.OfType<AIFunction>().ToList() ?? [];

        var showSpreadsheet = tools.FirstOrDefault(static tool => tool.Name == ShowSpreadsheetToolName);
        if (showSpreadsheet is not null
            && (lastUser.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase)
                || lastUser.Contains("excel", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", ShowSpreadsheetToolName, new Dictionary<string, object?>
                {
                    ["chatName"] = ChatNameFromContext(conversation),
                    ["title"] = "Yesterday",
                    ["sheetName"] = "Sheet1",
                    ["headers"] = "Item,Qty",
                    ["rows"] = "Shoes,2",
                }),
            ])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        // Precedence: spreadsheet/excel first, then "chart" beats "image".
        var renderChart = tools.FirstOrDefault(static tool => tool.Name == RenderChartToolName);
        if (renderChart is not null && lastUser.Contains("chart", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", RenderChartToolName, new Dictionary<string, object?>
                {
                    ["chatName"] = ChatNameFromContext(conversation),
                    ["title"] = "Test chart",
                    ["chartKind"] = "bar",
                    ["labels"] = new[] { "A", "B" },
                    ["values"] = new[] { 1.0, 2.0 },
                }),
            ])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        var generateImage = tools.FirstOrDefault(static tool => tool.Name == GenerateImageToolName);
        if (generateImage is not null && lastUser.Contains("image", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", GenerateImageToolName, new Dictionary<string, object?>
                {
                    ["chatName"] = ChatNameFromContext(conversation),
                    ["prompt"] = "Test image",
                }),
            ])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, Reply) { FinishReason = ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    // ChatTurnWorker's conversation-context line quotes the chat's full grain key right after
    // the word "chat" -- keep this pattern in sync with that literal. Scans every message
    // rather than trusting "the first system message" to be it: Agent.AskStreaming
    // prepends the assistant's OWN persona instructions as a system message ahead of
    // ChatTurnWorker's conversation-context one, so index 0 is the wrong message here.
    // Returning "main" when the pattern is absent from all of them is a loud failure
    // downstream (no such chat), never a silent wrong match.
    private static string ChatNameFromContext(IReadOnlyList<ChatMessage> conversation)
    {
        foreach (var message in conversation)
        {
            var match = ChatNameInContext().Match(message.Text);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return FallbackChatName;
    }

    [GeneratedRegex("chat '([^']+)'")]
    private static partial Regex ChatNameInContext();
}
