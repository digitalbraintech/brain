using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Core.Config;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Config;

// Per-pack config store with DataProtection encryption applied per value.
// Each value is protected with a per-key protector derived from purpose + key name,
// then the whole dictionary of (key → base64-ciphertext) is stored as a JSON blob.
public sealed class PackConfigStore(
    IDataProtectionProvider dpProvider,
    IPackConfigBackingStore backing,
    ILogger<PackConfigStore>? logger = null)
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

        // Decrypt per value: a value sealed under a now-unavailable key (rotated/recreated DataProtection key
        // ring, or written by a replica before the shared ring existed) must not poison the whole dictionary or
        // fault the caller's reactive loop. Skip the undecryptable value (never log its ciphertext) so callers
        // degrade to "that field isn't configured" and fall back to the global client.
        var result = new Dictionary<string, string>(encrypted.Count);
        foreach (var (key, ciphertext) in encrypted)
        {
            try
            {
                result[key] = ValueProtector(scope, pack, key).Unprotect(ciphertext);
            }
            catch (CryptographicException ex)
            {
                logger?.LogWarning(ex, "Could not decrypt config value '{Key}' for pack {Pack} (scope {Scope}); skipping.", key, pack, scope);
            }
        }

        return result;
    }
}
