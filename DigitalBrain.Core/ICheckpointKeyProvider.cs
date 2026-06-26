namespace DigitalBrain.Core;

// Source of the symmetric key for checkpoint encryption. Config-backed today; a Key Vault implementation
// drops in here without touching AddKernelSecurity. Null means "no key available".
public interface ICheckpointKeyProvider
{
    byte[]? GetKey();
}
