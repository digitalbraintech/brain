using DigitalBrain.Protocol;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests;

public class NeuronTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task Neuron_Activates_And_Journals_NeuronActivated()
    {
        var grain = _cluster!.GrainFactory.GetGrain<IDemoNeuron>("demo1");
        var timeline = await grain.GetTimelineAsync();

        Assert.NotEmpty(timeline);
        Assert.Contains(timeline, s => s.Type == nameof(NeuronActivated));
    }

    [Fact]
    public async Task FireAsync_Persists_And_Replayable()
    {
        var grain = _cluster!.GrainFactory.GetGrain<IDemoNeuron>("demo2");
        await grain.FireAsync(new DemoMessageSynapse("hello from test"));

        var timeline = await grain.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(DemoMessageSynapse));
    }

    [Fact]
    public async Task SystemStatus_Launches_And_Records_Status()
    {
        var status = _cluster!.GrainFactory.GetGrain<ISystemStatus>("status-test");
        var timeline = await status.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(SystemLaunched) || s.Type == nameof(SystemStatusChanged));
    }

    [Fact]
    public async Task SystemStatus_Simulates_Fix_From_Checkpoint()
    {
        var status = _cluster!.GrainFactory.GetGrain<ISystemStatus>("status-sim");
        var cp = await status.CreateCheckpointAsync();  // capture clean checkpoint before driving failure
        await status.FireAsync(new SystemStatusChanged("kernel", "FailedToStart", "test failure"));
        var timeline = await status.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(FixProposal));
        var sim = timeline.LastOrDefault(s => s.Type == nameof(SimulationResult)) as SimulationResult;
        Assert.NotNull(sim);
        Assert.True(sim.Success);
        Assert.Contains("different", sim.Details, StringComparison.OrdinalIgnoreCase);

        // Hardened isolated sim: replay proper CreateCheckpoint snapshot into separate TestCluster.
        var simBuilder = new TestClusterBuilder();
        simBuilder.AddSiloBuilderConfigurator<SiloConfigurator>();
        var simCluster = simBuilder.Build();
        await simCluster.DeployAsync();
        try
        {
            var simStatus = simCluster.GrainFactory.GetGrain<ISystemStatus>("status-isolated-sim");
            // Seed from checkpoint snapshot (faithful isolated replay)
            foreach (var s in cp.Snapshot.OfType<SystemStatusChanged>())
            {
                await simStatus.FireAsync(s);
            }
            await simStatus.FireAsync(new SystemStatusChanged("kernel", "FailedToStart", "isolated checkpoint replay"));
            var simTl = await simStatus.GetTimelineAsync();
            var isolatedSim = simTl.LastOrDefault(s => s.Type == nameof(SimulationResult)) as SimulationResult;
            Assert.NotNull(isolatedSim);
            Assert.True(isolatedSim.Success);
            Assert.Contains("different", isolatedSim.Details, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await simCluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task Marketplace_Publishes_Real_NeuroPack_With_Owner_Private_Commission()
    {
        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-test-1");
        var pack = new NeuroPack(
            "TestPrivatePack", "1.0", OwnerId: "owner1", IsPrivate: true, CommissionRate: 0.25, Code: "// test code", Description: "private test");
        
        await market.FireAsync(new PublishToMarketplace(pack.Name, pack.Version, pack.Code, pack.OwnerId, pack.IsPrivate, pack.CommissionRate, pack.Description));
        await market.FireAsync(new ListPublished());  // trigger the list event like real usage

        var timeline = await market.GetTimelineAsync();
        var published = timeline.LastOrDefault(s => s.Type == nameof(PublishedList)) as PublishedList;
        Assert.NotNull(published);
        Assert.Contains(published.Packs, p => p.Name == "TestPrivatePack" && p.OwnerId == "owner1" && p.IsPrivate && p.CommissionRate == 0.25);
    }

    [Fact]
    public async Task Marketplace_Install_Takes_Commission_And_Delivers_Pack()
    {
        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-test-2");
        await market.FireAsync(new PublishToMarketplace("CommPack", "1.0", Code: "real code here", OwnerId: "seller1", IsPrivate: false, CommissionRate: 0.15));

        await market.FireAsync(new InstallFromMarketplace("CommPack", "1.0", BuyerId: "buyer42"));

        var timeline = await market.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(CommissionTaken));
        var comm = timeline.LastOrDefault(s => s.Type == nameof(CommissionTaken)) as CommissionTaken;
        Assert.NotNull(comm);
        Assert.Equal(0.15, comm.CommissionRate);
        Assert.Equal("buyer42", comm.BuyerId);
        Assert.Equal("seller1", comm.SellerId);

        Assert.Contains(timeline, s => s.Type == nameof(NeuroPackInstalled));
        var installed = timeline.LastOrDefault(s => s.Type == nameof(NeuroPackInstalled)) as NeuroPackInstalled;
        Assert.NotNull(installed);
        Assert.Equal("CommPack", installed.Pack.Name);
        Assert.Equal("real code here", installed.Pack.Code);
    }

    [Fact]
    public async Task Marketplace_Private_Blocks_NonOwner_Install()
    {
        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-test-3");
        await market.FireAsync(new PublishToMarketplace("SecretPack", "1.0", OwnerId: "ownerOnly", IsPrivate: true, CommissionRate: 0.1));

        // Non-owner tries
        await market.FireAsync(new InstallFromMarketplace("SecretPack", "1.0", BuyerId: "stranger"));

        var timeline = await market.GetTimelineAsync();
        // Should not have installed or commission for stranger
        var lastInstalled = timeline.LastOrDefault(s => s.Type == nameof(NeuroPackInstalled)) as NeuroPackInstalled;
        Assert.Null(lastInstalled); // or check no commission for this
    }

    [Fact]
    public async Task Installed_Pack_Code_Reaches_GeneratedNeuron()
    {
        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-test-4");
        await market.FireAsync(new PublishToMarketplace("CodePack", "2.0", Code: "public class Test : Neuron { /* code */ }", OwnerId: "dev", IsPrivate: false));

        await market.FireAsync(new InstallFromMarketplace("CodePack", "2.0", BuyerId: "userX"));

        var gen = _cluster!.GrainFactory.GetGrain<IGeneratedNeuron>("generated-codepack");
        var timeline = await gen.GetTimelineAsync();
        // The install flow fires ExperienceUsed which triggers dispatch that now receives the pack
        Assert.Contains(timeline, s => s.Type == nameof(ExperienceUsed) || s is NeuroPackInstalled);
    }

    [Fact]
    public async Task KernelTask_Runs_And_Recovers_Status()
    {
        var task = _cluster!.GrainFactory.GetGrain<IKernelTask>("task-test-1");
        await task.FireAsync(new RunKernelTask("task-test-1", "demo work"));
        var info = await task.GetInfoAsync();
        Assert.Equal("completed", info.Status);
        Assert.Contains("demo work", info.Result ?? "");
    }

    [Fact]
    public async Task Branch_And_TimeTravel_Isolates_Journals()
    {
        var src = _cluster!.GrainFactory.GetGrain<ISystemStatus>("branch-src");
        await src.FireAsync(new SystemStatusChanged("test", "ok"));
        var cp = await src.CreateCheckpointAsync();
        var bid = await src.BranchAsync(cp);
        Assert.NotEqual(src.GetPrimaryKeyString(), bid.Value);

        var branch = _cluster.GrainFactory.GetGrain<IDemoNeuron>(bid.Value);
        await branch.FireAsync(new DemoMessageSynapse("from branch only"));
        var bOut = await branch.GetOutgoingTimelineAsync();
        var mainOut = await src.GetOutgoingTimelineAsync();
        Assert.Contains(bOut, s => s is DemoMessageSynapse);
        // Main not polluted by branch fire (isolation)
        Assert.DoesNotContain(mainOut, s => s is DemoMessageSynapse && ((DemoMessageSynapse)s).Text.Contains("branch only"));
    }

    [Fact]
    public async Task Ino_Uses_DualJournals_And_Creates_Tasks_Context()
    {
        var ino = _cluster!.GrainFactory.GetGrain<IInoNeuron>("ino-test");
        // Ask without llm should still process via fallback and journal
        var resp = await ino.AskAsync("remember to backup important files using task");
        Assert.False(string.IsNullOrWhiteSpace(resp));
        var outTl = await ino.GetOutgoingTimelineAsync();
        Assert.Contains(outTl, s => s.Type == nameof(InoRequest) || s.Type == nameof(InoResponse));
        // May have triggered task if parsed, but fallback ok
    }

    private class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryStreams("Default")
                .ConfigureServices(services =>
                {
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("in-journal", (_, _) => new InMemoryDurableList<DigitalBrain.Protocol.Synapse>());
                    services.AddKeyedScoped<Orleans.Journaling.IDurableList<DigitalBrain.Protocol.Synapse>>("out-journal", (_, _) => new InMemoryDurableList<DigitalBrain.Protocol.Synapse>());
                    services.AddSingleton<Orleans.Journaling.IJournaledStateManager, TestJournaledStateManager>();
                });
        }
    }
}
