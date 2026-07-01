using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

// Reads the AES checkpoint key (base64) from DigitalBrain:Checkpoint:Key (env/appsettings/Key Vault-mapped config).
public sealed class ConfigCheckpointKeyProvider(IConfiguration configuration) : ICheckpointKeyProvider
{
    public byte[]? GetKey()
    {
        var keyBase64 = configuration["DigitalBrain:Checkpoint:Key"];
        return string.IsNullOrWhiteSpace(keyBase64) ? null : Convert.FromBase64String(keyBase64);
    }
}
