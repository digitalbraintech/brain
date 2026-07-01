using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

// The real "N+1 reacts to a BROADCAST without restart" proof. An embodied pack whose manifest declares "Ping"
// must react to a broadcast Signal("Ping", ...) — without GeneratedNeuron statically declaring IHandle<Signal>.
// Installing a SECOND pack that also handles "Ping" makes the same broadcast reach N+1 embodied handlers.
public class PackBroadcastReactivityTests
{
    // Pack source compiled by the real embodier. Manifest handles "Ping"; Handle emits one PackEmission per
    // Signal("Ping", ...) and nothing otherwise. References only DigitalBrain.Core. Built by concat so no
    // escaped quotes are needed inside the raw string.
    private static string EchoPingPackSource(string packName)
    {
        const string q = "\"";
        return """
            using System.Collections.Generic;
            using DigitalBrain.Core;

            public sealed class PingEcho : IPackBehavior
            {
                public string Respond(string input) => "echo:" + (input ?? string.Empty);

                public PackManifest GetManifest() =>
                    new PackManifest(new[] { new SynapseType(
            """ + q + "Ping" + q + """
                    ) });

                public IReadOnlyList<Synapse> Handle(Synapse synapse)
                {
                    if (synapse is Signal sig && sig.Name ==
            """ + q + "Ping" + q + """
                    )
                    {
                        return new Synapse[] { new PackEmission(
            """ + q + packName + q + ", synapse.Type, " + q + "pong" + q + """
                        ) };
                    }
                    return System.Array.Empty<Synapse>();
                }
            }
            """;
    }

    public interface IPingBroadcaster : INeuron
    {
        Task EnsureActiveAsync();
        Task EmitPingAsync(string note);
    }

    public sealed class PingBroadcaster : Neuron, IPingBroadcaster
    {
        public PingBroadcaster(ILogger<PingBroadcaster> logger, NeuronJournals journals) : base(logger, journals) { }

        public Task EnsureActiveAsync() => Task.CompletedTask;

        public Task EmitPingAsync(string note) =>
            Broadcast(new Signal("Ping", new Dictionary<string, object?> { ["note"] = note }));
    }

    private static async Task<int> CountEmissionsAsync(IGeneratedNeuron grain) =>
        (await grain.GetTimelineAsync()).OfType<PackEmission>().Count();

    private static async Task<int> WaitForEmissionDeltaAsync(
        Func<Task<int>> totalEmissions, int baseline, int expectedDelta)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var current = await totalEmissions();
            if (current - baseline >= expectedDelta)
                return current;
            await Task.Delay(50);
        }
        return await totalEmissions();
    }

    [Fact]
    public async Task Embodied_Pack_Reacts_To_Broadcast_And_Adds_One_Responder_Per_Installed_Pack()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-pack-broadcast");

            const string pack1 = "PingEchoPackOne";
            await market.FireAsync(new PublishToMarketplace(pack1, "1.0", Code: EchoPingPackSource(pack1), OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
            await market.FireAsync(new InstallFromMarketplace(pack1, "1.0", BuyerId: "broadcast-user"));

            var gen1 = cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-" + pack1.ToLowerInvariant());

            // Snapshot AFTER install (install auto-activates the pack once via ExperienceUsed — that is not the broadcast we measure).
            var afterInstall1 = await CountEmissionsAsync(gen1);

            var emitter = cluster.GrainFactory.GetGrain<IPingBroadcaster>("ping-broadcaster");
            await emitter.EnsureActiveAsync();
            await emitter.EmitPingAsync("first");

            var afterBroadcast1 = await WaitForEmissionDeltaAsync(() => CountEmissionsAsync(gen1), afterInstall1, 1);
            Assert.Equal(1, afterBroadcast1 - afterInstall1);

            var lastEmission = (await gen1.GetTimelineAsync()).OfType<PackEmission>().Last();
            Assert.Equal(pack1, lastEmission.Pack);
            Assert.Equal("Ping", lastEmission.Input);
            Assert.Equal("pong", lastEmission.Output);

            // Install a SECOND pack also handling "Ping". The SAME broadcast must now reach N+1 embodied handlers.
            const string pack2 = "PingEchoPackTwo";
            await market.FireAsync(new PublishToMarketplace(pack2, "1.0", Code: EchoPingPackSource(pack2), OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
            await market.FireAsync(new InstallFromMarketplace(pack2, "1.0", BuyerId: "broadcast-user"));

            var gen2 = cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-" + pack2.ToLowerInvariant());

            var totalAfterInstall2 = await CountEmissionsAsync(gen1) + await CountEmissionsAsync(gen2);

            await emitter.EmitPingAsync("second");

            Task<int> TotalAcrossBothAsync() =>
                Task.Run(async () => await CountEmissionsAsync(gen1) + await CountEmissionsAsync(gen2));

            var totalAfterBroadcast2 = await WaitForEmissionDeltaAsync(TotalAcrossBothAsync, totalAfterInstall2, 2);
            Assert.Equal(2, totalAfterBroadcast2 - totalAfterInstall2);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
