using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Foundry;

[GrainType("digitalbrain.foundry.loop.v1")]
public class CodeFoundryClosedLoopNeuron(ILogger<CodeFoundryClosedLoopNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICodeFoundryLoopNeuron
{
    public async Task HandleAsync(FoundryRequest request)
    {
        var checkpoint = await CreateCheckpointAsync();
        var checkpointId = checkpoint.Timestamp.ToUnixTimeMilliseconds().ToString();
        await FireAsync(new FoundryCheckpointed(request.Spec, checkpointId));

        var codeGen = GrainFactory.GetGrain<ICodeGenNeuron>("foundry-codegen");
        await codeGen.FireAsync(new GenerateCode(request.Spec, request.Tier));
        var generated = (await codeGen.GetOutgoingTimelineAsync())
            .OfType<CodeGenerated>()
            .LastOrDefault(g => g.Spec == request.Spec);

        if (generated is null)
        {
            await FireAsync(new FoundryRolledBack(request.Spec, "no-source", checkpointId));
            return;
        }

        if (request.Tier == TargetTier.Run)
        {
            var runner = GrainFactory.GetGrain<ICodeRunNeuron>("foundry-coderun");
            await runner.FireAsync(new RunGeneratedCode(generated.Source));
            var runResult = (await runner.GetOutgoingTimelineAsync()).OfType<CodeRunResult>().LastOrDefault();

            if (runResult is { Success: true })
                await FireAsync(new FoundryCompleted(request.Spec, request.Tier, runResult.Output, Applied: true));
            else
                await FireAsync(new FoundryRolledBack(request.Spec, runResult?.Error ?? "run-failed", checkpointId));
            return;
        }

        var moduleName = StableModuleName(request.Spec);
        var deployer = GrainFactory.GetGrain<ICodeDeployNeuron>("foundry-codedeploy");
        await deployer.FireAsync(new DeployGeneratedCode(generated.Source, moduleName, CheckpointId: checkpointId));
        var built = (await deployer.GetOutgoingTimelineAsync()).OfType<CodeBuilt>().LastOrDefault(b => b.ModuleName == moduleName);

        if (built is { Success: true })
            await FireAsync(new FoundryCompleted(request.Spec, request.Tier, "restart-requested:" + moduleName, Applied: true));
        else
            await FireAsync(new FoundryRolledBack(request.Spec, "build", checkpointId));
    }

    private static string StableModuleName(string spec)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(spec));
        return "Gen_" + Convert.ToHexString(bytes)[..12];
    }

    // Note on resume-after-restart: in production a Tier-2 restart interrupts this handler after
    // SiloRestartRequested. On reactivation the orchestrator re-reads its journal; because
    // FoundryCompleted is fired immediately after a successful CodeBuilt (before the physical restart
    // completes), the cycle's terminal synapse is already journaled and the loop does not re-run.
    // The Tier-2 scenario here asserts the CodeBuilt/restart path via the deploy neuron (Task 7);
    // end-to-end restart survival is covered by the manual validation in Task 10.
}

