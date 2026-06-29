using System;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class CheckpointKeyingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var dict = new System.Collections.Generic.Dictionary<string, string?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void ConfigProvider_Returns_The_Configured_Key()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var provider = new ConfigCheckpointKeyProvider(Config(("DigitalBrain:Checkpoint:Key", key)));
        Assert.Equal(new byte[32], provider.GetKey());
    }

    [Fact]
    public void With_Key_Registers_Aes_Protector_That_RoundTrips()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKernelSecurity(Config(("DigitalBrain:Checkpoint:Key", key)), new FakeHostEnvironment("Production"));
        using var sp = services.BuildServiceProvider();

        var protector = sp.GetRequiredService<INeuronStateProtector>();
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };
        Assert.Equal(plaintext, protector.Unprotect(protector.Protect(plaintext)));
        Assert.IsType<AesNeuronStateProtector>(protector);
    }

    [Fact]
    public void Production_Without_Key_Fails_Fast()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddKernelSecurity(Config(), new FakeHostEnvironment("Production")));
    }

    [Fact]
    public void Development_Without_Key_Falls_Back_To_PassThrough()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKernelSecurity(Config(), new FakeHostEnvironment("Development"));
        using var sp = services.BuildServiceProvider();
        Assert.IsType<PassThroughNeuronStateProtector>(sp.GetRequiredService<INeuronStateProtector>());
    }
}
