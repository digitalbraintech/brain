namespace DigitalBrain.Core;

// Built-in trusted-publisher identity for FIRST-PARTY seeds and the kernel pack only. Lets secure-by-default
// (reject unsigned) ship without breaking the system's own preinstalled packs. NOT a production secret:
// third-party/remote publishers sign with their own keys via PackSignatureVerifier; real cloud trust keying
// is a separate (deferred) Key Vault concern.
public static class TrustedPublisher
{
    private const string PrivateKeyBase64 = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgIClVkj1jM5gq7zh8lWJjhUolM/iqXfYFOwzKpASZTtyhRANCAASYPKi1tTyPiqa458/f7QEj2QQ9GzLsf2h4lzPmhzTWWFTkBXsivTDQ2C7oml//OZjtbUgaLul0Sbr4pmpDpwo2";
    public const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEmDyotbU8j4qmuOfP3+0BI9kEPRsy7H9oeJcz5oc01lhU5AV7Ir0w0Ngu6Jpf/zmY7W1IGi7pdEm6+KZqQ6cKNg==";

    public static PublishToMarketplace SignPublishCommand(PublishToMarketplace command)
    {
        var content = PackSignatureVerifier.CanonicalContent(
            command.PackName, command.Version, command.Code, PublicKeyBase64);
        return command with
        {
            AuthorPublicKeyBase64 = PublicKeyBase64,
            SignatureBase64 = PackSignatureVerifier.Sign(content, PrivateKeyBase64)
        };
    }
}
