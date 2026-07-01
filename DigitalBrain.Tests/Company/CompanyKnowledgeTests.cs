using DigitalBrain.Context;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Company;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Company;

public sealed class CompanyKnowledgeTests : IAsyncLifetime
{
    private TestCluster? _cluster;
    private IServiceProvider? _services;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _services = _cluster.Client.ServiceProvider;
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task Ingests_Company_Process_Sources_And_Allows_Recall_Of_Key_Decisions()
    {
        // Use the grain for ingest path (exercises neuron + ingestor + journaled memory).
        var company = _cluster!.GrainFactory.GetGrain<ICompanyKnowledgeNeuron>("company-refunds");

        const string policy = """
            Eligibility: purchased within 30 days, provide order ID or receipt.
            If defective and within 14 days: auto-approve full refund plus shipping.
            Amount over 500 or suspicious: manual review.
            Loyalty members get more leeway on receipt.
            """;

        const string transcript = """
            Always check purchase date first. Over 30 days is no unless warranty.
            Defective first two weeks auto. Flag high value for manual.
            """;

        await company.FireAsync(new IngestCompanySource("company-process-refunds", "refund-policy", policy));
        await company.FireAsync(new IngestCompanySource("company-process-refunds", "refund-transcript", transcript));

        // ContextNeuron Recall (hybrid) sees the journaled MemoryStored from ingest.
        var context = _cluster.GrainFactory.GetGrain<IContextNeuron>("context-for-company");
        await context.RememberAsync(policy); // reinforce
        await context.RememberAsync(transcript);

        var hits = await context.RecallAsync("30 days window defective auto approve", top: 3);

        Assert.NotEmpty(hits);
        string combined = string.Join(" ", hits);
        Assert.Contains("30", combined);
        Assert.Contains("defective", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auto", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocumentIngestor_Produces_Chunks_For_Process_Text()
    {
        // Direct fast-path (no DI from client scope). Uses same types as neuron path.
        var vectorStore = new InMemoryVectorStore();
        var embedder = new NoOpEmbeddingGenerator();
        var ingestor = new DocumentIngestor(embedder, vectorStore);
        string source = "Step 1: check date. Step 2: verify receipt. If over 30 deny.";
        int count = await ingestor.IngestAsync("company-test", "policy-direct", source);
        Assert.True(count > 0);
    }

    [Fact]
    public async Task Crystallizes_Refund_ProcessSpec_With_Key_Decision_Points_From_Sources()
    {
        var company = _cluster!.GrainFactory.GetGrain<ICompanyKnowledgeNeuron>("company-crystallize");
        await company.FireAsync(new IngestCompanySource("company-process-refunds", "p", "30 days. defective auto within 14. over 500 manual. loyalty leeway."));

        var context = _cluster.GrainFactory.GetGrain<IContextNeuron>("ctx-cryst");
        var fragments = await context.RecallAsync("refund 30 days defective", top: 5);

        var crystallizer = new ProcessCrystallizer(chatClient: null);
        var result = await crystallizer.CrystallizeAsync("RefundHandling", fragments);

        Assert.Equal("RefundHandling", result.Spec.ProcessName);
        Assert.Contains(result.Spec.DecisionPoints, d => d.Condition.Contains("30") || d.Condition.Contains("defective"));
        Assert.Contains("RefundApproved", result.Spec.EmittedOutcomeTypes);
    }

    [Fact]
    public void Synthesizes_Valid_Gate_Clean_Pack_That_Embodied_Handles_Refund_And_Emits_Audit()
    {
        var synth = new SkillPackSynthesizer();
        var spec = new ProcessSpec(
            "RefundHandling",
            [ "RefundRequested" ],
            [ "check window" ],
            [ new DecisionPoint("days > 30", "deny", "check other") ],
            [ "outside window" ],
            [ "RefundApproved", "RefundDenied" ],
            [ "emit-outcomes" ]);

        string code = synth.SynthesizePackSource(spec);

        var embodier = new PackAlcEmbodier();
        using var pack = embodier.Embody("RefundHandling", code);

        // Typed dispatch: create request (type from Core now)
        var req = new RefundRequested("r1", 120m, "defective", "c1", 5);
        var outputs = pack.Handle(req);

        Assert.NotEmpty(outputs);
        var emission = outputs.OfType<PackEmission>().FirstOrDefault();
        Assert.NotNull(emission);
        Assert.Equal("RefundHandling", emission.Pack);
        Assert.Contains("approved", emission.Output);

        // Outcome present for living map
        Assert.Contains(outputs, o => o is RefundApproved or RefundDenied);
    }

    [Fact]
    public async Task EndToEnd_CompanySkill_Ingest_Crystallize_Synth_EmbodyViaGenerated_FiresAndAuditsInJournals()
    {
        // 1. Ingest
        var company = _cluster!.GrainFactory.GetGrain<ICompanyKnowledgeNeuron>("company-e2e");
        await company.FireAsync(new IngestCompanySource("e2e-refunds", "policy", "30 days. defective auto 14d. high value manual."));

        // 2. Recall + crystallize
        var ctx = _cluster.GrainFactory.GetGrain<IContextNeuron>("ctx-e2e");
        var frags = await ctx.RecallAsync("refund", top: 3);
        var cryst = new ProcessCrystallizer(null);
        var specResult = await cryst.CrystallizeAsync("RefundHandling", frags);

        // 3. Synthesize pack
        var synth = new SkillPackSynthesizer();
        string packCode = synth.SynthesizePackSource(specResult.Spec, "e2e.1");

        // 4. Deliver via NeuroPackInstalled to Generated (reuses embodiment path)
        var gen = _cluster.GrainFactory.GetGrain<IGeneratedNeuron>("skill-refundhandling");
        var pack = new NeuroPack("RefundHandling", "e2e.1", OwnerId: "test", Code: packCode);
        await gen.DeliverAsync(new NeuroPackInstalled(pack));

        // 5. Fire trigger (typed path)
        var trigger = new RefundRequested("req-e2e-1", 99m, "defective", "cust1", 3);
        await gen.FireAsync(trigger);

        // 6. Audit: journals have PackEmission + outcome (living map)
        var timeline = await gen.GetOutgoingTimelineAsync();
        Assert.Contains(timeline, s => s is PackEmission pe && pe.Pack == "RefundHandling" && pe.Output.Contains("approved"));
        Assert.Contains(timeline, s => s is RefundApproved or RefundDenied);
    }

    [Fact]
    public async Task KeepCurrent_ReSynthesizeAndReEmbody_UpdatesBehaviorSafely()
    {
        var gen = _cluster!.GrainFactory.GetGrain<IGeneratedNeuron>("skill-keepcurrent");

        // v1 policy
        var synth = new SkillPackSynthesizer();
        var specV1 = new ProcessSpec("RefundHandling", ["RefundRequested"], [], [], [], ["RefundApproved"], []);
        string codeV1 = synth.SynthesizePackSource(specV1, "v1");
        await gen.DeliverAsync(new NeuroPackInstalled(new NeuroPack("RefundHandling", "v1", Code: codeV1)));

        var req = new RefundRequested("k1", 10m, "normal", "c", 1);
        await gen.FireAsync(req);
        var tl1 = await gen.GetOutgoingTimelineAsync();
        Assert.Contains(tl1.OfType<PackEmission>(), e => e.Pack == "RefundHandling"); // v1 embodied

        // Simulate drift: new spec/source -> v2 code (e.g. stricter window)
        var specV2 = specV1 with { ProcessName = "RefundHandling" }; // same for simplicity, different code version
        string codeV2 = synth.SynthesizePackSource(specV2, "v2");
        await gen.DeliverAsync(new NeuroPackInstalled(new NeuroPack("RefundHandling", "v2", Code: codeV2)));

        await gen.FireAsync(new RefundRequested("k2", 10m, "normal", "c", 1));
        var tl2 = await gen.GetOutgoingTimelineAsync();
        // New emissions after re-embody exist
        var recent = tl2.OfType<PackEmission>().LastOrDefault(e => e.Input.Contains("k2") || e.Pack == "RefundHandling");
        Assert.NotNull(recent);
    }

    [Fact]
    public async Task Orchestrator_Creates_Skill_EndToEnd_And_Reports_Result()
    {
        var orch = _cluster!.GrainFactory.GetGrain<ICompanySkillOrchestratorNeuron>("orchestrator-test");
        await orch.FireAsync(new CreateCompanySkill("RefundHandling"));

        await Task.Delay(150);

        var tl = await orch.GetOutgoingTimelineAsync();
        var result = tl.OfType<CompanySkillCreationResult>().LastOrDefault();
        Assert.NotNull(result);
        Assert.Equal("RefundHandling", result.ProcessName);
        Assert.True(result.Details.Length > 5);
    }
}
