using System.Security.Cryptography;
using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// AES-GCM protector with a shared symmetric key. Cross-platform and round-trips across silo nodes (unlike DPAPI),
// so it fits the distributed Aspire/ACA silo. The key is supplied from config (Key Vault in cloud).
public sealed class AesNeuronStateProtector : INeuronStateProtector
{
    private const int NonceSize = 12;  // AES-GCM standard nonce
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesNeuronStateProtector(byte[] key)
    {
        if (key.Length is not (16 or 24 or 32))
            throw new ArgumentException("AES key must be 128/192/256-bit.", nameof(key));
        _key = key;
    }

    public byte[] Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: nonce | tag | ciphertext
        var output = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceSize);
        ciphertext.CopyTo(output, NonceSize + TagSize);
        return output;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < NonceSize + TagSize)
            throw new ArgumentException("Ciphertext is too short.", nameof(ciphertext));

        var nonce = ciphertext.AsSpan(0, NonceSize);
        var tag = ciphertext.AsSpan(NonceSize, TagSize);
        var payload = ciphertext.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[payload.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, payload, tag, plaintext);  // throws on a bad tag (tampering) — fail-fast
        return plaintext;
    }
}

// No-op protector for local dev/test when no key is configured. NOT encryption — registered only with an
// explicit logged warning so the absence of protection is visible (not a silent dev fallback).
public sealed class PassThroughNeuronStateProtector : INeuronStateProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;
    public byte[] Unprotect(byte[] ciphertext) => ciphertext;
}
