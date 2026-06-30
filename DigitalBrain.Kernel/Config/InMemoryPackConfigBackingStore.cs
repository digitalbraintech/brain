using System.Collections.Concurrent;

namespace DigitalBrain.Kernel.Config;

public sealed class InMemoryPackConfigBackingStore : IPackConfigBackingStore
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    private static string BlobKey(string scope, string pack) => $"{scope}/{pack}";

    public Task<byte[]?> LoadAsync(string scope, string pack)
        => Task.FromResult<byte[]?>(_store.TryGetValue(BlobKey(scope, pack), out var blob) ? blob : null);

    public Task SaveAsync(string scope, string pack, byte[] encryptedBlob)
    {
        _store[BlobKey(scope, pack)] = encryptedBlob;
        return Task.CompletedTask;
    }

    // Exposed for tests to assert encryption actually happened.
    public byte[]? Peek(string scope, string pack)
        => _store.TryGetValue(BlobKey(scope, pack), out var blob) ? blob : null;
}
