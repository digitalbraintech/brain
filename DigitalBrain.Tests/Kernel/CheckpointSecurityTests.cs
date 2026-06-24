using DigitalBrain.Protocol;
using DigitalBrain.Silo;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class CheckpointSecurityTests
{
    private static byte[] TestKey => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Aes_Protector_RoundTrips_And_Detects_Tampering()
    {
        var protector = new AesNeuronStateProtector(TestKey);
        var plaintext = System.Text.Encoding.UTF8.GetBytes("secret checkpoint state");

        var ciphertext = protector.Protect(plaintext);
        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, protector.Unprotect(ciphertext));

        ciphertext[^1] ^= 0xFF; // tamper with the authenticated ciphertext
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(ciphertext));
    }

    [Fact]
    public void CheckpointProtector_Encrypts_And_Restores_Polymorphic_Snapshot()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b => b.AddAssembly(typeof(Synapse).Assembly));
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer>();

        var protector = new CheckpointProtector(serializer, new AesNeuronStateProtector(TestKey));
        var snapshot = new List<Synapse>
        {
            new DemoMessageSynapse("hello"),
            new LlmResponse("prompt", "response", "model")
        };
        var checkpoint = new Checkpoint(new NeuronId("n1"), snapshot.AsReadOnly(), DateTimeOffset.UtcNow);

        var encrypted = protector.Protect(checkpoint);
        var restored = protector.Unprotect(encrypted);

        Assert.Equal(2, restored.Snapshot.Count);
        Assert.Contains(restored.Snapshot, s => s is DemoMessageSynapse d && d.Text == "hello");
        Assert.Contains(restored.Snapshot, s => s is LlmResponse r && r.Response == "response");
        Assert.Equal("n1", restored.Source.Value);
    }
}
