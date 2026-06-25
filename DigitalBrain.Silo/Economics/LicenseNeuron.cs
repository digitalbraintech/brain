using System.Text;
using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Silo;

// The license server. Issues ECDSA-signed, portable license tokens (reusing PackSignatureVerifier) and records
// each grant in its journal; entitlement (HasLicense) is journal-derived. The signing key pair is persisted in
// the journal on first use (prod should source it from Key Vault).
[GrainType("digitalbrain.license.v1")]
public class LicenseNeuron : Neuron, ILicenseNeuron
{
    public LicenseNeuron(ILogger<LicenseNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task<string> IssueLicenseAsync(string bundleId, string userId)
    {
        var (privateKey, _) = await EnsureKeysAsync();

        var payloadJson = JsonSerializer.Serialize(new LicensePayload(
            bundleId, userId, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N")));
        var signature = PackSignatureVerifier.Sign(payloadJson, privateKey);
        var tokenJson = JsonSerializer.Serialize(new LicenseTokenData(payloadJson, signature));
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenJson));

        await FireAsync(new LicenseGranted(bundleId, userId, token));
        Logger.LogInformation("Issued license for user '{User}' on '{Bundle}'", userId, bundleId);
        return token;
    }

    public async Task<bool> VerifyLicenseAsync(string licenseToken, string bundleId, string userId)
    {
        var (_, publicKey) = await EnsureKeysAsync();
        try
        {
            var tokenJson = Encoding.UTF8.GetString(Convert.FromBase64String(licenseToken));
            var token = JsonSerializer.Deserialize<LicenseTokenData>(tokenJson);
            if (token is null) return false;

            // Verify the signature against THIS server's public key (not a token-embedded one).
            if (!PackSignatureVerifier.Verify(token.Payload, token.Signature, publicKey)) return false;

            var payload = JsonSerializer.Deserialize<LicensePayload>(token.Payload);
            return payload is not null
                && string.Equals(payload.BundleId, bundleId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(payload.UserId, userId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // A malformed token is "invalid", not a crash. Other exceptions propagate (fail-fast).
            return false;
        }
    }

    public Task<bool> HasLicenseAsync(string bundleId, string userId)
    {
        var granted = OutgoingJournal.Concat(IncomingJournal).OfType<LicenseGranted>().Any(l =>
            string.Equals(l.BundleId, bundleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.UserId, userId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(granted);
    }

    private async Task<(string PrivateKey, string PublicKey)> EnsureKeysAsync()
    {
        var existing = OutgoingJournal.Concat(IncomingJournal).OfType<LicenseKeyPair>().LastOrDefault();
        if (existing is not null)
            return (existing.PrivateKeyBase64, existing.PublicKeyBase64);

        var (privateKey, publicKey) = PackSignatureVerifier.GenerateKeyPair();
        await FireAsync(new LicenseKeyPair(privateKey, publicKey));
        Logger.LogInformation("Generated persistent ECDSA key pair for the license server.");
        return (privateKey, publicKey);
    }

    private sealed record LicensePayload(string BundleId, string UserId, DateTimeOffset IssuedAt, string Nonce);
    private sealed record LicenseTokenData(string Payload, string Signature);
}

