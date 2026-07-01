using DigitalBrain.Core;
namespace DigitalBrain.Kernel;

[GrainType("ino.code.editor.v1")]
public class InoCodeEditorNeuron(ILogger<InoCodeEditorNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IInoCodeEditor
{
    public async Task HandleAsync(InoCodeEdit cmd)
    {
        Logger.LogInformation("INO Code Editor edit for {Id}", cmd.EditorId);
        await FireAsync(new InoCodeEdit(cmd.EditorId, cmd.Code, cmd.Language));
        await FireAsync(new ContextUpdate("editor", "lastCode", cmd.Code.Length > 120 ? cmd.Code[..120] + "..." : cmd.Code));
    }

    public async Task HandleAsync(InoCodeRun cmd)
    {
        Logger.LogInformation("INO Code Editor run for {Id}: {Result}", cmd.EditorId, cmd.Result);
        await FireAsync(cmd);
    }

    public async Task HandleAsync(InoCodeSave cmd)
    {
        Logger.LogInformation("INO Code Editor save {Name} for {Id}", cmd.ExperienceName, cmd.EditorId);
        await FireAsync(cmd);
        var market = GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(cmd.ExperienceName, "0.1-ino", cmd.Code, "editor-user", false, 0.0, cmd.Description));
        await FireAsync(new ContextUpdate("editor", "saved", cmd.ExperienceName));
    }

    public async Task HandleAsync(InoCodeExecute cmd)
    {
        Logger.LogInformation("INO Code Editor execute for {Id}", cmd.EditorId);
        await FireAsync(cmd);
        var compiler = GrainFactory.GetGrain<ICompiler>("compiler-main");
        await compiler.FireAsync(new CreateNeuronRequest(cmd.Instruction + " | editor:" + cmd.EditorId, "csharp"));
        await FireAsync(new InoCodeRun(cmd.EditorId, "executed-via-compiler"));
    }

    public async Task HandleAsync(InoCodeApplySkill cmd)
    {
        Logger.LogInformation("INO Code Editor apply skill {Skill} for {Id}", cmd.SkillPackName, cmd.EditorId);
        var market = GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new ListPublished());
        var tl = await market.GetTimelineAsync();
        var list = tl.LastOrDefault(s => s is PublishedList) as PublishedList;
        var pack = list?.Packs.FirstOrDefault(p => p.Name.Equals(cmd.SkillPackName, StringComparison.OrdinalIgnoreCase));
        if (pack != null)
        {
            await FireAsync(new SkillContextInjected(pack.Name, pack.Description, pack.Code));
            await FireAsync(new ContextUpdate("editor-skill", pack.Name, pack.Description.Length > 80 ? pack.Description[..80] : pack.Description));
            var gen = GrainFactory.GetGrain<IGeneratedNeuron>("generated-" + pack.Name.ToLowerInvariant());
            await gen.FireAsync(new ExperienceUsed(pack.Name, "editor-apply"));
        }
        else
        {
            await FireAsync(new ContextUpdate("editor-skill", cmd.SkillPackName, "not-found-in-journals"));
        }
        await FireAsync(new InoCodeRun(cmd.EditorId, "skill-applied:" + cmd.SkillPackName));
    }
}


