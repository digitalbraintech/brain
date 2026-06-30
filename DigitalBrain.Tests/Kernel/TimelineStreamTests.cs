using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

// Verifies that the DigitalBrainTimeline stream provider is registered and the Timeline() extension resolves the correct stream.
public class TimelineStreamTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TimelineClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    [Fact]
    public void ProviderName_Is_DigitalBrainTimeline()
    {
        Assert.Equal("DigitalBrainTimeline", SynapseStream.ProviderName);
    }

    [Fact]
    public void Timeline_Extension_Returns_Stream_For_Global_Namespace()
    {
        var provider = _cluster.Client.GetStreamProvider(SynapseStream.ProviderName);
        var stream = provider.Timeline();

        Assert.NotNull(stream);
        Assert.Equal("global", stream.StreamId.GetKeyAsString());
    }
}

file sealed class TimelineClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
        clientBuilder.AddMemoryStreams(SynapseStream.ProviderName);
}
