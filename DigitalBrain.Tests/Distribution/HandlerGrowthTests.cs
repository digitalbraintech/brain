using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class HandlerGrowthTests
{
    [Fact]
    public async Task Installing_A_Pack_Adds_Exactly_One_Responder_To_A_Previously_Unhandled_Synapse()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            const string packCode = """
                public sealed class Echoer : DigitalBrain.Core.IPackBehavior
                {
                    public string Respond(string input) => "echo:" + (input ?? string.Empty);
                }
                """;

            var gen = cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-echopackn1");

            // Before install: firing the trigger produces no responder.
            await gen.FireAsync(new ExperienceUsed("EchoPackN1", "before"));
            var beforeResponders = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();
            Assert.Equal(0, beforeResponders);

            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-n1");
            await market.FireAsync(new PublishToMarketplace("EchoPackN1", "1.0", Code: packCode, OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
            await market.FireAsync(new InstallFromMarketplace("EchoPackN1", "1.0", BuyerId: "n1-user"));

            // Snapshot AFTER install (install auto-activates the pack once — that emission is not what we measure).
            var afterInstall = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();

            // A single post-install broadcast must reach exactly one new responder (the embodied pack).
            await gen.FireAsync(new ExperienceUsed("EchoPackN1", "after"));
            var afterFire = (await gen.GetTimelineAsync()).OfType<PackEmission>().Count();
            Assert.Equal(1, afterFire - afterInstall);

            var lastEmission = (await gen.GetTimelineAsync()).OfType<PackEmission>().Last();
            Assert.Equal("EchoPackN1", lastEmission.Pack);
            Assert.Equal("echo:after", lastEmission.Output);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task Dev_Can_Package_And_Publish_Dummy_Distributions_Using_Seeds_Helpers()
    {
        // This exercises the core dev-on-DigitalBrain case: packaging a new kernel version or behavior pack
        // for publish (share to marketplace) and install.
        var kernelCmd = MarketplaceSeeds.KernelPublishCommand("0.4.0-dev");
        Assert.Equal("kernel", kernelCmd.PackName);
        Assert.Contains("0.4.0-dev", kernelCmd.Version);

        var dummyCmd = MarketplaceSeeds.DummyBehaviorPackPublish();
        Assert.Equal("Dummy.DevPack", dummyCmd.PackName);
        Assert.Contains("IPackBehavior", dummyCmd.Code); // proves the Code packaging for full typed C# dummy
        Assert.True(dummyCmd.CommissionRate > 0);

        // In real dev: fire to market grain (local or via remote proxy to private marketplace repo).
        // Here we just validate the "packaging" step produces valid commands.
    }
}
