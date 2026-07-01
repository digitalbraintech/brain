using DigitalBrain.Core;

namespace DigitalBrain.Tests.Trust;

public class PackSignatureTests
{
    [Fact]
    public void SignedPack_Verifies_And_Tamper_Is_Detected()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var pack = new NeuroPack("Echo", "1.0", OwnerId: "dev", Code: "public class Echo {}");
        var signed = PackSignatureVerifier.SignPack(pack, priv, pub);

        Assert.True(PackSignatureVerifier.VerifyPack(signed));

        // Tampering with the code after signing breaks verification.
        var tampered = signed with { Code = signed.Code + " // evil" };
        Assert.False(PackSignatureVerifier.VerifyPack(tampered));

        // A different author key cannot validate someone else's signature.
        var (_, otherPub) = PackSignatureVerifier.GenerateKeyPair();
        var wrongKey = signed with { AuthorPublicKeyBase64 = otherPub };
        Assert.False(PackSignatureVerifier.VerifyPack(wrongKey));

        // An unsigned pack does not verify (it is allowed only by the marketplace transition policy).
        Assert.False(PackSignatureVerifier.VerifyPack(new NeuroPack("Echo", "1.0")));
    }
}

