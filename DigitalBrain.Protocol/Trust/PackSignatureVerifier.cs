using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Protocol;

// ECDSA-nistP256 + SHA256 signing/verification for marketplace packs. BCL-only (no BouncyCastle).
// Ported from digitalbrain BundleSignatureVerifier; surfaces base64 strings because NeuroPack and the
// publish/install synapses carry strings and serialize trivially over Orleans + the JSON journal.
public static class PackSignatureVerifier
{
    // The canonical bytes a pack signature covers: identity + code integrity + author key, pipe-joined.
    public static string CanonicalContent(string name, string version, string code, string authorPublicKeyBase64)
        => $"{name}|{version}|{ContentHash(code)}|{authorPublicKeyBase64}";

    public static string ContentHash(string? code)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code ?? string.Empty)));

    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
                Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    public static string Sign(string content, string privateKeyBase64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(content), HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signature);
    }

    // Signs a pack at publish time: binds Name|Version|Hash(Code)|PublicKey and embeds the author key + signature.
    public static NeuroPack SignPack(NeuroPack pack, string privateKeyBase64, string publicKeyBase64)
    {
        var content = CanonicalContent(pack.Name, pack.Version, pack.Code, publicKeyBase64);
        return pack with
        {
            AuthorPublicKeyBase64 = publicKeyBase64,
            SignatureBase64 = Sign(content, privateKeyBase64)
        };
    }

    // Verifies a pack carries a valid author signature over its current code. False if unsigned or tampered.
    public static bool VerifyPack(NeuroPack pack)
    {
        if (string.IsNullOrEmpty(pack.AuthorPublicKeyBase64) || string.IsNullOrEmpty(pack.SignatureBase64))
            return false;
        var content = CanonicalContent(pack.Name, pack.Version, pack.Code, pack.AuthorPublicKeyBase64);
        return Verify(content, pack.SignatureBase64, pack.AuthorPublicKeyBase64);
    }

    public static bool Verify(string content, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(content),
                Convert.FromBase64String(signatureBase64),
                HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // A malformed key/signature from an untrusted publisher is a verification FAILURE, not a crash.
            // Only these narrow input-shape exceptions are treated as "invalid"; anything else propagates.
            return false;
        }
    }
}
