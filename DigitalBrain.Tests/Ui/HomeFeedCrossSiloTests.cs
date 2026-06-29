using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class HomeFeedCrossSiloTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder(initialSilosCount: 2);
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.StopAllSilosAsync();

    [Fact]
    public async Task Broadcast_On_Silo0_Received_On_Silo1()
    {
        var bus0 = ((InProcessSiloHandle)_cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();
        var bus1 = ((InProcessSiloHandle)_cluster.Silos[1]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        using var subscription = bus1.Subscribe();
        var card = new RfwCard("digitalbrain", "CrossSiloCard", "{\"x\":1}");

        bus0.Broadcast(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("CrossSiloCard", received.RootWidget);
    }

    [Fact]
    public async Task Broadcast_On_Silo0_Also_Delivered_To_Silo0_Subscriber()
    {
        // In cluster mode Broadcast goes out via the stream and loops back through this silo's own subscriber,
        // so a client connected to the producing silo must still receive the card (no synchronous local fanout).
        var bus0 = ((InProcessSiloHandle)_cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        using var subscription = bus0.Subscribe();
        var card = new RfwCard("digitalbrain", "SelfDeliveryCard", "{\"x\":2}");

        bus0.Broadcast(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("SelfDeliveryCard", received.RootWidget);
    }
}
