namespace DigitalBrain.Core;

// Encryption-at-rest for protected checkpoints / neuron state. Abstraction ported from digitalbrain's
// INeuronStateProtector. Implementations: AES-GCM (shared key) for the distributed silo, PassThrough for dev.
public interface INeuronStateProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
