using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Foundry;

[GrainType("digitalbrain.codedeploy.v1")]
public class CodeDeployNeuron(ILogger<CodeDeployNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICodeDeployNeuron
{
    public async Task HandleAsync(DeployGeneratedCode cmd)
    {
        if (RestartPending())
        {
            await FireAsync(new FoundryRolledBack(cmd.ModuleName, "deploy-in-progress", cmd.CheckpointId));
            return;
        }

        var buildRunner = ServiceProvider.GetRequiredService<IBuildRunner>();
        var outcome = await buildRunner.VerifyBuildAsync(cmd.ModuleName, cmd.Source);
        await FireAsync(new CodeBuilt(cmd.ModuleName, outcome.Success, outcome.Log));

        if (!outcome.Success)
        {
            await FireAsync(new FoundryRolledBack(cmd.ModuleName, "build", cmd.CheckpointId));
            return;
        }

        CommitSource(cmd.ModuleName, cmd.Source);

        var resourceController = ServiceProvider.GetRequiredService<IResourceController>();
        await resourceController.RestartSiloAsync("apply-" + cmd.ModuleName);
        await FireAsync(new SiloRestartRequested("apply-" + cmd.ModuleName, cmd.ModuleName));
    }

    private bool RestartPending()
    {
        var lastRestart = OutgoingJournal.OfType<SiloRestartRequested>().LastOrDefault();
        if (lastRestart is null) return false;
        var lastActivated = OutgoingJournal.OfType<NeuronActivated>().LastOrDefault();
        return lastActivated is null || lastRestart.Timestamp >= lastActivated.Timestamp;
    }

    private static void CommitSource(string moduleName, string source)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Generated");
        // Resolve to the source tree Generated folder when running from the silo project.
        var projectGenerated = Path.Combine(Directory.GetCurrentDirectory(), "Generated");
        var target = Directory.Exists(projectGenerated) ? projectGenerated : dir;
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, moduleName + ".cs"), source);
    }
}

