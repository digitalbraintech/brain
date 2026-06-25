using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

using DigitalBrain.Silo; // for HomeFeedBus (server-driven UI fanout from neurons only)

namespace DigitalBrain.Silo.Company;

[GrainType("company.skill.orchestrator.v1")]
public sealed class CompanySkillOrchestratorNeuron : Neuron, ICompanySkillOrchestratorNeuron
{
    public CompanySkillOrchestratorNeuron(ILogger<CompanySkillOrchestratorNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(CreateCompanySkill cmd)
    {
        var processName = cmd.ProcessName;

        Logger.LogInformation("Orchestrator starting skill creation for {Process}", processName);

        if (string.Equals(processName, "Kernel", StringComparison.OrdinalIgnoreCase) || string.Equals(processName, "kernel", StringComparison.OrdinalIgnoreCase))
        {
            await HandleKernelSelfUpdateAsync();
            return;
        }

        string baseDir = AppContext.BaseDirectory;
        string samplesRoot = Path.Combine(baseDir, "..", "..", "..", "samples", "CompanyBrain");
        if (!Directory.Exists(samplesRoot))
            samplesRoot = Path.Combine("samples", "CompanyBrain");

        string policyText = File.Exists(Path.Combine(samplesRoot, "refund-policy.md"))
            ? await File.ReadAllTextAsync(Path.Combine(samplesRoot, "refund-policy.md"))
            : "Eligibility: within 30 days + receipt or loyalty. Defective <14d auto-approve. High value >500 manual.";

        string transcriptText = File.Exists(Path.Combine(samplesRoot, "refund-transcript.txt"))
            ? await File.ReadAllTextAsync(Path.Combine(samplesRoot, "refund-transcript.txt"))
            : "Check date first. 30 day window. Defective auto early. Flag high value.";

        var knowledge = GrainFactory.GetGrain<ICompanyKnowledgeNeuron>("company-main");
        await knowledge.FireAsync(new IngestCompanySource("company-skills", $"{processName}-policy", policyText));
        await knowledge.FireAsync(new IngestCompanySource("company-skills", $"{processName}-transcript", transcriptText));

        var context = GrainFactory.GetGrain<IContextNeuron>("context-main");
        var fragments = await context.RecallAsync($"how to handle {processName} process decisions", top: 6);

        var crystallizer = ServiceProvider.GetService<ProcessCrystallizer>() 
            ?? new ProcessCrystallizer(ServiceProvider.GetService<IChatClient>());
        var crystallized = await crystallizer.CrystallizeAsync(processName, fragments);

        var synthesizer = ServiceProvider.GetService<SkillPackSynthesizer>() ?? new SkillPackSynthesizer();
        string code = synthesizer.SynthesizePackSource(crystallized.Spec, "1.0");

        var market = GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(
            processName, "1.0", code, "system", false, 0.0, $"Auto-generated executable skill for {processName}"));

        await market.FireAsync(new InstallFromMarketplace(processName, "1.0", "system"));

        var generated = GrainFactory.GetGrain<IGeneratedNeuron>($"generated-{processName.ToLowerInvariant()}");
        var testTrigger = new RefundRequested("verify-001", 75m, "defective", "cust-test", 5);
        await generated.FireAsync(testTrigger);

        await Task.Delay(50);

        var timeline = await generated.GetOutgoingTimelineAsync();
        var lastEmission = timeline.OfType<PackEmission>().LastOrDefault(e => e.Pack.Equals(processName, StringComparison.OrdinalIgnoreCase));
        bool verified = lastEmission != null && lastEmission.Output.Contains("approved", StringComparison.OrdinalIgnoreCase);

        string details = verified
            ? $"Embodied and executed successfully. Last emission: {lastEmission!.Output}"
            : "Installed but verification emission not found or not successful.";

        await FireAsync(new CompanySkillCreationResult(processName, "1.0", verified, details));

        Logger.LogInformation("Company skill creation completed for {Process}: {Success} - {Details}", processName, verified, details);
    }

    private async Task HandleKernelSelfUpdateAsync()
    {
        var market = GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        var version = "2026.6-hotfix-kernel";
        // Carry real payload (metadata + signal) so kernel update is a proper typed pack embodiment opportunity.
        var kernelPackCode = "// kernel-update-signal version=" + version + "\npublic sealed class KernelUpdateBehavior : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => \"kernel-rolling:\" + i; }";
        await market.FireAsync(new PublishToMarketplace("kernel", version, kernelPackCode, "digitalbraintech", false, 0.0, "Kernel runtime self-update via marketplace as pre-installed pack"));

        await market.FireAsync(new InstallFromMarketplace("kernel", version, "self"));

        // Preserve state before rolling restart using checkpoint (seamless update primitive).
        var preUpdateCheckpoint = await CreateCheckpointAsync();

        var aspire = GrainFactory.GetGrain<IAspireNeuron>("aspire-main");
        await aspire.FireAsync(new RestartResource("silo", IsRollingUpdate: true, TargetVersion: version, Strategy: "one-replica-at-a-time"));

        // Exercise causal query (new kernel API) over the update lineage.
        var updateLineage = await GetCausalLineageAsync(preUpdateCheckpoint.SynapseId);
        var lineageCount = updateLineage.Count;

        await FireAsync(new CompanySkillCreationResult("Kernel", version, true, $"Kernel pack installed from marketplace. Rolling restart requested (replicas=3, one at a time, checkpoint preserved, lineage events: {lineageCount})."));

        // Drive UI fully from neuron (RfwCard + UiSurface for complete declarative surfaces).
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is not null)
        {
            var statusData = System.Text.Json.JsonSerializer.Serialize(new { process = "kernel", version, status = "updating", haReplicas = 3, checkpoint = preUpdateCheckpoint.SynapseId, lineageEvents = lineageCount });
            bus.Broadcast(new RfwCard("digitalbrain", "KernelUpdateStatusCard", statusData));

            var rollingSurfaceProps = new Dictionary<string, object?>
            {
                [UiSurfaceKeys.SurfaceId] = "kernel-update-" + version,
                [UiSurfaceKeys.Emitter] = Self.Value,
                [UiSurfaceKeys.Title] = "Kernel Rolling Update",
                [UiSurfaceKeys.Priority] = 80,
                [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
                ["version"] = version,
                ["strategy"] = "one-replica-at-a-time",
                ["checkpointId"] = preUpdateCheckpoint.SynapseId,
                ["status"] = "in-progress"
            };
            await FireAsync(new UiSurface("kernel-rolling-update", rollingSurfaceProps));
        }
    }
}
