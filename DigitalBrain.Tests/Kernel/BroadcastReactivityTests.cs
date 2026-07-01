using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Kernel;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

[GenerateSerializer]
public record Ping(string Note) : Synapse(nameof(Ping), DateTimeOffset.UtcNow, IsBroadcast: true);

public interface IPingSink : INeuron
{
    Task EnsureActiveAsync();
    Task<int> ReceivedCountAsync();
}

public interface IPingEmitter : INeuron
{
    Task EnsureActiveAsync();
    Task EmitPingAsync(string note);
}

public sealed class PingSink : Neuron, IPingSink, IHandle<Ping>
{
    private int _received;

    public PingSink(ILogger<PingSink> logger, NeuronJournals journals) : base(logger, journals) { }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task<int> ReceivedCountAsync() => Task.FromResult(_received);

    public Task HandleAsync(Ping synapse)
    {
        _received++;
        return Task.CompletedTask;
    }
}

public sealed class PingEmitter : Neuron, IPingEmitter
{
    public PingEmitter(ILogger<PingEmitter> logger, NeuronJournals journals) : base(logger, journals) { }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task EmitPingAsync(string note) => Broadcast(new Ping(note));
}

public class BroadcastReactivityTests
{
    private static async Task WaitForCountAsync(Func<Task<int>> getCount, int expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await getCount() >= expected)
                return;
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Broadcast_reaches_every_activated_handler()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var a = cluster.GrainFactory.GetGrain<IPingSink>("a");
            var b = cluster.GrainFactory.GetGrain<IPingSink>("b");
            await a.EnsureActiveAsync();
            await b.EnsureActiveAsync();
            var emitter = cluster.GrainFactory.GetGrain<IPingEmitter>("e");
            await emitter.EmitPingAsync("hello");
            await WaitForCountAsync(() => a.ReceivedCountAsync(), 1);
            await WaitForCountAsync(() => b.ReceivedCountAsync(), 1);
            Assert.Equal(1, await a.ReceivedCountAsync());
            Assert.Equal(1, await b.ReceivedCountAsync());
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
