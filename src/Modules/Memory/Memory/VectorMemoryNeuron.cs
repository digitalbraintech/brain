using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Memory;

[GrainType("vectormemory")]
public sealed class VectorMemoryNeuron :
    Neuron,
    IVectorMemory
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddings;
    private readonly IVectorMemoryStore _store;

    public VectorMemoryNeuron(NeuronRuntime runtime)
        : base(runtime)
    {
        _embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        _store = ServiceProvider.GetRequiredService<IVectorMemoryStore>();
    }

    // Missing configuration must refuse (settled), never crash-retry: an
    // unconfigured capability is a conversation with the owner, not a storm.
    private IEmbeddingGenerator<string, Embedding<float>> RequireEmbeddings()
        => _embeddings
            ?? throw new NeuronAuthorizationException(
                $"Vector memory '{Id}' has no embedding model. Wire an "
                + "IEmbeddingGenerator (an Ollama embedding model in the AppHost) "
                + "and try again.");

    public async Task HandleAsync(StoreVectorMemory signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateStore(signal);

        if (IsReserved(signal.Namespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(
                new VectorMemoryStored(
                    Stored: false,
                    signal.Namespace,
                    signal.Key,
                    VectorMemoryStoreStatus.ReservedNamespace))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var embedding = await EmbedAsync(signal.Text, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = signal.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(signal.Metadata, StringComparer.Ordinal);

        await _store.UpsertAsync(
            new VectorMemoryEntry(
                Id.Owner.Value,
                signal.Namespace.Value,
                signal.Key,
                signal.Text,
                metadata,
                signal.Payload,
                embedding),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(
            new VectorMemoryStored(
                Stored: true,
                signal.Namespace,
                signal.Key,
                VectorMemoryStoreStatus.Stored))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(SearchVectorMemory signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSearch(signal);

        var queryEmbedding = await EmbedAsync(signal.Query, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = await _store.SearchAsync(
            Id.Owner.Value,
            signal.Namespace.Value,
            queryEmbedding,
            signal.Limit,
            signal.Metadata,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(new VectorMemoryMatches(signal.Namespace, matches))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(RemoveVectorMemory signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRemove(signal);

        if (IsReserved(signal.Namespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReplyAsync(new VectorMemoryRemoved(Removed: false, signal.Namespace, signal.Key))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var removed = await _store.RemoveAsync(
            Id.Owner.Value,
            signal.Namespace.Value,
            signal.Key,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(new VectorMemoryRemoved(removed, signal.Namespace, signal.Key))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var generated = await RequireEmbeddings().GenerateAsync(
            [text],
            cancellationToken: cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return generated[0].Vector.ToArray();
    }

    private static bool IsReserved(VectorMemoryNamespace ns) =>
        ns == VectorMemoryNamespace.Capabilities;

    private static void ValidateStore(StoreVectorMemory request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
    }

    private static void ValidateSearch(SearchVectorMemory request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Limit, 1);
    }

    private static void ValidateRemove(RemoveVectorMemory request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Key);
    }
}
