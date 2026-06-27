namespace DigitalBrain.Core;

// ECDSA-signed software licenses for premium marketplace packs. Reuses PackSignatureVerifier (ECDSA-nistP256).
// Harvested from digitalbrain's LicenseNeuron (token shape), re-homed onto MAIN's Neuron/journal model — the
// entitlement source of truth is the journal (LicenseGranted), not a Postgres DB.
public interface ILicenseNeuron : INeuron
{
    // Issue a signed, portable license token for (bundleId, userId) and record the grant. Returns the token.
    Task<string> IssueLicenseAsync(string bundleId, string userId);

    // Verify a portable token: signature (against the license server's own key) + payload match.
    Task<bool> VerifyLicenseAsync(string licenseToken, string bundleId, string userId);

    // In-cluster entitlement check: has a license been granted to userId for bundleId?
    Task<bool> HasLicenseAsync(string bundleId, string userId);

    // Overloads using the Core UserId contract (preferred for new code; strings kept for compat).
    Task<string> IssueLicenseAsync(string bundleId, UserId userId) => IssueLicenseAsync(bundleId, userId.Value);
    Task<bool> VerifyLicenseAsync(string licenseToken, string bundleId, UserId userId) => VerifyLicenseAsync(licenseToken, bundleId, userId.Value);
    Task<bool> HasLicenseAsync(string bundleId, UserId userId) => HasLicenseAsync(bundleId, userId.Value);
}

// Journal record of an issued license — the entitlement source of truth for install gating.
[GenerateSerializer]
public record LicenseGranted(string BundleId, string UserId, string Token)
    : Synapse(nameof(LicenseGranted), DateTimeOffset.UtcNow)
{
    public LicenseGranted(string bundleId, UserId userId, string token) : this(bundleId, userId.Value, token) { }
}

// The license server's persistent ECDSA key pair (journal-persisted). Production should source the key from
// Key Vault rather than the journal; this is the dev/self-contained path.
[GenerateSerializer]
public record LicenseKeyPair(string PrivateKeyBase64, string PublicKeyBase64)
    : Synapse(nameof(LicenseKeyPair), DateTimeOffset.UtcNow);
