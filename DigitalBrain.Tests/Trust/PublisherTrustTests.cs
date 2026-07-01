using DigitalBrain.Core;

namespace DigitalBrain.Tests.Trust;

public class PublisherTrustTests
{
    private static NeuroPack Pack(string code = "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }")
        => new("p", "1.0.0", Code: code);

    [Fact]
    public void Signed_by_a_key_on_the_allowlist_is_trusted()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(Pack(), priv, pub);

        Assert.True(PublisherTrust.IsTrusted(signed, new[] { pub }));
    }

    [Fact]
    public void Signed_by_a_key_not_on_the_allowlist_is_untrusted()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var (_, otherPub) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(Pack(), priv, pub);

        Assert.False(PublisherTrust.IsTrusted(signed, new[] { otherPub }));
    }

    [Fact]
    public void Unsigned_is_untrusted()
    {
        Assert.False(PublisherTrust.IsTrusted(Pack(), new[] { "any-key" }));
    }

    [Fact]
    public void Tampered_code_after_signing_is_untrusted()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(Pack(), priv, pub);
        var tampered = signed with { Code = signed.Code + " // sneaky" };

        Assert.False(PublisherTrust.IsTrusted(tampered, new[] { pub }));
    }
}
