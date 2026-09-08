using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests;

// One scripted step of a model's behaviour. A test writes the script up front and the client
// consumes exactly one item per request, so an agent's inner function-call loop is driven turn
// by turn rather than by pattern-matching on prompts.
internal abstract record ScriptItem
{
    internal sealed record Say(string Text) : ScriptItem;

    internal sealed record CallTool(string Name, string ArgumentsJson) : ScriptItem;

    internal sealed record Pause : ScriptItem;
}

internal sealed class ScriptedChatClient : IChatClient
{
    private readonly ConcurrentQueue<ScriptItem> _script = new();
    private readonly List<IReadOnlyList<ChatMessage>> _calls = [];
    private readonly List<ChatOptions?> _options = [];
    private readonly Lock _gate = new();
    private TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every request the model received, in order, as a snapshot of its messages.</summary>
    public IReadOnlyList<IReadOnlyList<ChatMessage>> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>The options of every request, in order, alongside <see cref="Calls"/>.</summary>
    public IReadOnlyList<ChatOptions?> Options
    {
        get
        {
            lock (_gate)
            {
                return [.. _options];
            }
        }
    }

    public void Say(string text) => _script.Enqueue(new ScriptItem.Say(text));

    public void CallTool(string name, string argumentsJson) => _script.Enqueue(new ScriptItem.CallTool(name, argumentsJson));

    public void Pause() => _script.Enqueue(new ScriptItem.Pause());

    public void Unpause() => _paused.TrySetResult();

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        lock (_gate)
        {
            _calls.Add([.. messages]);
            _options.Add(options);
        }

        while (_script.TryDequeue(out var item))
        {
            switch (item)
            {
                case ScriptItem.Say say:
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, say.Text)]);
                case ScriptItem.CallTool tool:
                    var call = new FunctionCallContent(
                        Guid.NewGuid().ToString("N"),
                        tool.Name,
                        Arguments(tool.ArgumentsJson));
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, [call])]);
                default:
                    // A pause blocks the turn where a real model would still be thinking, so a
                    // test can assert on what the brain does while an answer is outstanding.
                    await _paused.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    break;
            }
        }

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, string.Empty)]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey) => null;

    public void Dispose()
    {
    }

    private static Dictionary<string, object?> Arguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject node
            ? []
            : node.ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value?.ToString(), StringComparer.Ordinal);
    }
}
