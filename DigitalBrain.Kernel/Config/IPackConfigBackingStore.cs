namespace DigitalBrain.Kernel.Config;

// Persists opaque encrypted blobs keyed by (scope, pack). The PackConfigStore owns encryption;
// the backing store only moves bytes around.
public interface IPackConfigBackingStore
{
    Task<byte[]?> LoadAsync(string scope, string pack);
    Task SaveAsync(string scope, string pack, byte[] encryptedBlob);
}
