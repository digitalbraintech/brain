using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Kernel;

public class RollingUpdateRollbackTests
{
    [Fact]
    public async Task Verify_Failure_Rolls_Back_And_Does_Not_Complete()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var aspire = cluster.GrainFactory.GetGrain<IAspireNeuron>("aspire-rollback");
            await aspire.FireAsync(new DigitalBrain.Core.PerformKernelSelfUpdate("rollback-test", FailAtReplica: 2));

            var timeline = await aspire.GetTimelineAsync();
            var kinds = timeline.OfType<UiSurface>().Select(s => s.Kind).ToArray();

            Assert.Contains(KernelUiSurfaceKinds.RollingRollback, kinds);
            Assert.DoesNotContain(KernelUiSurfaceKinds.RollingComplete, kinds);
            // Replica 1 drained before the failure at replica 2; replica 3 never started.
            Assert.Contains(timeline.OfType<UiSurface>(),
                s => s.Kind == KernelUiSurfaceKinds.RollingDrain && Equals(s.Props.GetValueOrDefault("replica"), 1));
            Assert.DoesNotContain(timeline.OfType<UiSurface>(),
                s => s.Kind == KernelUiSurfaceKinds.RollingDrain && Equals(s.Props.GetValueOrDefault("replica"), 3));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
