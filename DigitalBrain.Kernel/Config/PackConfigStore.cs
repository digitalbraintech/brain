using System.Text.Json;
using DigitalBrain.Core.Config;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Kernel.Config;

// Per-pack config store with DataProtection encryption applied per value.
// Each value is protected with a per-key protector derived from purpose + key name,
// then the whole dictionary of (key → base64-ciphertext) is stored as a JSON blob.
public sealed class PackConfigStore(IDataProtectionProvider dpProvider, IPackConfigBackingStore backing)
    : IPackConfigStore
{
    private const string RootPurpose = "DigitalBrain.PackConfig";

    private IDataProtector ValueProtector(string scope, string pack, string key)
        => dpProvider.CreateProtector(RootPurpose, scope, pack, key);

    public async Task SetAsync(string scope, string pack, IReadOnlyDictionary<string, string> values)
    {
        var encrypted = values.ToDictionary(
            kv => kv.Key,
            kv => ValueProtector(scope, pack, kv.Key).Protect(kv.Value));

        var blob = JsonSerializer.SerializeToUtf8Bytes(encrypted);
        await backing.SaveAsync(scope, pack, blob);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string scope, string pack)
    {
        var blob = await backing.LoadAsync(scope, pack);
        if (blob is null)
            return new Dictionary<string, string>();

        var encrypted = JsonSerializer.Deserialize<Dictionary<string, string>>(blob)
            ?? new Dictionary<string, string>();

        return encrypted.ToDictionary(
            kv => kv.Key,
            kv => ValueProtector(scope, pack, kv.Key).Unprotect(kv.Value));
    }
}
