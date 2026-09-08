using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests;

internal sealed class ScriptedChatClient(string reply) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, reply)]));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(ChatRole.Assistant, reply);
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey) => null;

    public void Dispose()
    {
    }
}
