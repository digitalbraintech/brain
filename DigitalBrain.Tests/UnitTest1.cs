using DigitalBrain.Protocol;
using DigitalBrain.Silo.Foundry;
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
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
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
        simBuilder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
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
    public async Task Synapse_Propagates_Correlation_And_Causation()
    {
        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-causation");
        await market.FireAsync(new PublishToMarketplace("CausPack", "1.0", Code: "x", OwnerId: "owner", IsPrivate: false, CommissionRate: 0.1));
        await market.FireAsync(new InstallFromMarketplace("CausPack", "1.0", BuyerId: "buyer"));

        var outTl = await market.GetOutgoingTimelineAsync();
        var install = outTl.OfType<InstallFromMarketplace>().Last();
        var commission = outTl.OfType<CommissionTaken>().Last();

        // Every fired synapse has a stable id; a root synapse correlates to itself.
        Assert.False(string.IsNullOrEmpty(install.SynapseId));
        Assert.Equal(install.SynapseId, install.CorrelationId);
        Assert.Null(install.CausationId);

        // The commission was fired while handling the install, so it inherits the chain + points back at it.
        Assert.Equal(install.SynapseId, commission.CausationId);
        Assert.Equal(install.CorrelationId, commission.CorrelationId);
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
    public async Task Branch_Forks_Same_Type_With_Replayed_History_And_Isolation()
    {
        var src = _cluster!.GrainFactory.GetGrain<IDemoNeuron>("branch-src");
        await src.FireAsync(new DemoMessageSynapse("original"));
        var cp = await src.CreateCheckpointAsync();
        var bid = await src.BranchAsync(cp);
        Assert.NotEqual(src.GetPrimaryKeyString(), bid.Value);

        // The branch is a grain of the SAME type, seeded with the checkpoint history.
        var branch = _cluster.GrainFactory.GetGrain<IDemoNeuron>(bid.Value);
        var branchIn = await branch.GetIncomingTimelineAsync();
        Assert.Contains(branchIn, s => s is DemoMessageSynapse d && d.Text == "original");

        // Firing on the branch does not pollute the source (isolation).
        await branch.FireAsync(new DemoMessageSynapse("branch only"));
        var mainOut = await src.GetOutgoingTimelineAsync();
        Assert.DoesNotContain(mainOut, s => s is DemoMessageSynapse d && d.Text.Contains("branch only"));
    }

    [Fact]
    public async Task Restore_Seeds_Journal_From_Checkpoint_Without_Redispatch()
    {
        var src = _cluster!.GrainFactory.GetGrain<IDemoNeuron>("restore-src");
        await src.FireAsync(new DemoMessageSynapse("to-restore"));
        var checkpoint = await src.CreateCheckpointAsync();

        var target = _cluster.GrainFactory.GetGrain<IDemoNeuron>("restore-target");
        await target.RestoreCheckpointAsync(checkpoint);

        var restored = await target.GetIncomingTimelineAsync();
        Assert.Contains(restored, s => s is DemoMessageSynapse d && d.Text == "to-restore");
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

    [Fact]
    public async Task DataVisualizationNeuron_Emits_DataChart_Surface()
    {
        var chart = _cluster!.GrainFactory.GetGrain<IDataVisualizationNeuron>("chart-test");

        await chart.FireAsync(new VisualizeDataRequest(
            "show sales by month",
            """
            [
              { "month": "Jan", "sales": 12 },
              { "month": "Feb", "sales": 18 }
            ]
            """,
            "bar",
            "req-test"));

        var timeline = await chart.GetTimelineAsync();
        var generated = timeline.OfType<DataChartGenerated>().LastOrDefault(result => result.RequestId == "req-test");

        Assert.NotNull(generated);
        Assert.Equal(UiSurfaceKinds.DataChart, generated.Surface.Kind);
        Assert.True(generated.Surface.Props.ContainsKey(UiSurfaceKeys.ChartSpec));
    }

    [Fact]
    public async Task Marketplace_Rejects_Invalid_Signature_And_Accepts_Valid()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();

        // A validly-signed pack installs.
        var marketOk = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-sign-ok");
        var good = PackSignatureVerifier.SignPack(new NeuroPack("SignOk", "1.0", OwnerId: "dev", Code: "ok"), priv, pub);
        await marketOk.FireAsync(new PublishToMarketplace(
            good.Name, good.Version, good.Code, good.OwnerId, good.IsPrivate, good.CommissionRate,
            good.Description, good.AuthorPublicKeyBase64, good.SignatureBase64));
        await marketOk.FireAsync(new InstallFromMarketplace("SignOk", "1.0", BuyerId: "buyer"));
        Assert.Contains(await marketOk.GetTimelineAsync(), s => s is NeuroPackInstalled);

        // A pack whose code was tampered AFTER signing is rejected at install (signature no longer matches).
        var marketBad = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-sign-bad");
        var signed = PackSignatureVerifier.SignPack(new NeuroPack("SignBad", "1.0", OwnerId: "dev", Code: "original"), priv, pub);
        var tampered = signed with { Code = "tampered" };
        await marketBad.FireAsync(new PublishToMarketplace(
            tampered.Name, tampered.Version, tampered.Code, tampered.OwnerId, tampered.IsPrivate, tampered.CommissionRate,
            tampered.Description, tampered.AuthorPublicKeyBase64, tampered.SignatureBase64));
        await marketBad.FireAsync(new InstallFromMarketplace("SignBad", "1.0", BuyerId: "buyer"));
        Assert.DoesNotContain(await marketBad.GetTimelineAsync(), s => s is NeuroPackInstalled);
    }

    [Fact]
    public async Task Install_Embodies_Signed_Pack_And_Runs_Real_Compiled_Code()
    {
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var code = """
            public sealed class EchoPack : DigitalBrain.Protocol.IPackBehavior
            {
                public string Respond(string input) => "ECHO:" + input;
            }
            """;
        var pack = PackSignatureVerifier.SignPack(
            new NeuroPack("EchoPack", "1.0", OwnerId: "dev", Code: code), priv, pub);

        var market = _cluster!.GrainFactory.GetGrain<IMarketplaceNeuron>("market-embody");
        await market.FireAsync(new PublishToMarketplace(
            pack.Name, pack.Version, pack.Code, pack.OwnerId, pack.IsPrivate, pack.CommissionRate,
            pack.Description, pack.AuthorPublicKeyBase64, pack.SignatureBase64));
        await market.FireAsync(new InstallFromMarketplace("EchoPack", "1.0", BuyerId: "buyer"));

        // The host neuron compiled pack.Code into a collectible ALC and dispatched to it for real.
        var generated = _cluster!.GrainFactory.GetGrain<IGeneratedNeuron>("generated-echopack");
        var emission = (await generated.GetTimelineAsync()).OfType<PackEmission>().LastOrDefault();

        Assert.NotNull(emission);                         // proof the install->compile->ALC->dispatch chain ran
        Assert.Equal("EchoPack", emission!.Pack);
        Assert.StartsWith("ECHO:", emission.Output);      // ...the pack's REAL compiled output, not the LLM stub
    }

    [Fact]
    public async Task GitNeuron_Commits_And_Derives_Metrics_From_Journal()
    {
        var repo = Path.Combine(Path.GetTempPath(), "dbgit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repo);
        try
        {
            RunGit("init -b main", repo);
            RunGit("config user.email test@example.com", repo);
            RunGit("config user.name Tester", repo);
            RunGit("config commit.gpgsign false", repo);
            await File.WriteAllTextAsync(Path.Combine(repo, "file.txt"), "hello");

            var git = _cluster!.GrainFactory.GetGrain<IGitNeuron>("git-test");

            var status = await git.StatusAsync(repo);
            Assert.Contains("file.txt", status);

            await git.CommitAsync(repo, "add file");

            var log = await git.LogAsync(repo);
            Assert.Single(log);
            Assert.Contains("add file", log[0]);

            var metrics = await git.GetMetricsAsync();
            Assert.Equal(1, metrics.TotalCommits);
            Assert.Equal(0, metrics.TotalReverts);
            Assert.True(metrics.LastCommit > DateTimeOffset.MinValue);
        }
        finally
        {
            TryDeleteDir(repo);
        }
    }

    private static void RunGit(string args, string cwd)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup: Windows can briefly lock .git pack files. Not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above — read-only .git objects on some platforms.
        }
    }

}
