using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class UnconfiguredChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "Set DigitalBrain:AI:ApiKey or XAI_API_KEY before asking an llm or groupchat neuron.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey) => null;

    public void Dispose()
    {
    }
}
