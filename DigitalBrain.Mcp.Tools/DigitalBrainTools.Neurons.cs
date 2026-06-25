using DigitalBrain.Core;
using ModelContextProtocol.Server;
using Orleans;
using System.ComponentModel;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

public partial class DigitalBrainTools
{
    [McpServerTool(Name = "ping_digitalbrain"), Description("Simple ping tool to verify MCP connection to DigitalBrain server works. Always returns success.")]
    public static string PingDigitalBrain() => "DigitalBrain MCP connected successfully. Cluster interaction tools ready when silo is running.";

    [McpServerTool(Name = "ask_llm_neuron"), Description("Ask the LLM neuron (powered by local Qwen/Ollama) a question or prompt. Returns the response. Requires the cluster (silo + ollama) to be running.")]
    public async Task<string> AskLlmNeuron(
        [Description("The prompt or question to send to the LLM neuron")] string prompt,
        [Description("Optional preferred model, e.g. 'qwen2.5-coder:1.5b'")] string? preferredModel = null)
    {
        var llm = grains.GetGrain<ILlmNeuron>("llm-main");
        await llm.FireAsync(new LlmPrompt(prompt, preferredModel));

        var response = (await llm.GetTimelineAsync()).OfType<LlmResponse>().LastOrDefault();
        return response is not null
            ? $"LLM Response (model: {response.ModelUsed}):\n{response.Response}"
            : "Prompt fired to the LLM neuron, but no response is on the timeline yet (is Ollama running?).";
    }

    [McpServerTool(Name = "fire_synapse"), Description("Fire a synapse (message) to any neuron by ID. Use for demo, system, marketplace etc. Returns confirmation.")]
    public async Task<string> FireSynapse(
        [Description("Neuron ID / grain key, e.g. 'demo-opt', 'llm-main', 'market-main'")] string neuronId,
        [Description("The text or payload for the synapse (for DemoMessageSynapse)")] string text)
    {
        var neuron = ResolveNeuron(neuronId);
        await neuron.FireAsync(new DemoMessageSynapse(text));
        return $"Successfully fired DemoMessageSynapse with text '{text}' to neuron '{neuronId}'.";
    }

    [McpServerTool(Name = "get_timeline"), Description("Get recent timeline (synapses) for a neuron. Useful to see history, responses, published packs etc.")]
    public async Task<string> GetTimeline(
        [Description("Neuron ID to query, e.g. 'llm-main', 'market-main', 'compiler-main'")] string neuronId,
        [Description("Max number of recent entries")] int maxEntries = 10)
    {
        var neuron = ResolveNeuron(neuronId);
        var timeline = await neuron.GetTimelineAsync();
        var lines = timeline.TakeLast(maxEntries).Select(s => $"{s.Timestamp:HH:mm:ss} | {s.Type}: {s}");
        return string.Join("\n", lines);
    }

    [McpServerTool(Name = "ask_ino"), Description("Ask the INO AI assistant (uses ContextNeuron for smart management).")]
    public Task<string> AskIno([Description("Prompt for INO navigation/assistant")] string prompt)
        => grains.GetGrain<IInoNeuron>("ino-main").AskAsync(prompt);

    [McpServerTool(Name = "ino_code_editor"), Description("Interact with the INOCodeEditor neuron for visual editing/running of pack code.")]
    public async Task<string> InoCodeEditor([Description("Editor ID")] string id, [Description("Code or command")] string code)
    {
        var editor = grains.GetGrain<IInoCodeEditor>("ino-editor-main");
        await editor.FireAsync(new InoCodeEdit(id, code));
        return $"INOCodeEditor received edit for {id}. Run to execute.";
    }

    [McpServerTool(Name = "update_context_filter"), Description("Update ContextNeuron (e.g. when a UI filter changes so INO sees it).")]
    public async Task<string> UpdateContextFilter(
        [Description("Filter/view name")] string view,
        [Description("Filter key")] string filter,
        [Description("Value")] string val)
    {
        var context = grains.GetGrain<IContextNeuron>("context-main");
        await context.FireAsync(new ContextUpdate("filter:" + view, filter, val));
        await context.FireAsync(new FilterChanged(view, filter, val)); // notify for LLM awareness
        return $"Context+Filter updated for {view}. INO/Context now aware.";
    }

    [McpServerTool(Name = "db_example"), Description("Exercise the DbSupportNeuron (connect + typed query via synapses).")]
    public async Task<string> DbExample([Description("Connection name e.g. northwind")] string name, [Description("Query")] string query)
    {
        var db = grains.GetGrain<IDbSupportNeuron>("db-main");
        await db.FireAsync(new DbConnect(name, "sqlite", "Data Source=:memory:"));
        await db.FireAsync(new DbQuery(name, query));
        return "DB neuron handled connect+query via typed synapses. Check timeline for results.";
    }

    [McpServerTool(Name = "cluster_3d_activity"), Description("Fire activity for the 3D graph in the UI kit (connects to cluster observation).")]
    public async Task<string> Cluster3D(
        [Description("Node ID")] string node,
        [Description("Activity type")] string activity,
        [Description("Value")] double value)
    {
        var vis = ResolveNeuron("cluster-vis");
        await vis.FireAsync(new ClusterActivity(node, activity, value));
        await vis.FireAsync(new ThreeDGraphUpdate("main", JsonSerializer.Serialize(new { node, activity, value })));
        return "Cluster activity sent for 3D visualization.";
    }

    [McpServerTool(Name = "run_closed_loop"), Description("Trigger a marketplace closed loop ('ui' for Dart MCP widget-tree authoring, 'se' for SoftwareEngineering runtime mod via Aspire MCP + LLM).")]
    public async Task<string> RunClosedLoop(
        [Description("Loop type: ui | se")] string loopType,
        [Description("Prompt or task for the loop, e.g. inspect editor tree and improve")] string prompt)
    {
        var loop = grains.GetGrain<IClosedLoopNeuron>("closedloop-main");
        await loop.FireAsync(new ClosedLoopRequest(loopType, prompt));
        return $"ClosedLoop {loopType} triggered on the marketplace-installed experience.";
    }

    [McpServerTool(Name = "dart_ui_inspect_and_reload"), Description("Helper for the UI closed loop: connect Dart DTD, get the live widget tree, and hot reload after mods.")]
    public static string DartUIInspect(
        [Description("DTD uri from running flutter (copy from IDE or console)")] string dtdUri,
        [Description("Whether to hot reload after inspect")] bool doReload = false)
        => $"[UIClosedLoop] Connect dart DTD {dtdUri}, call get_widget_tree(summaryOnly=true), then hot_reload after the LLM proposes edits (doReload={doReload}).";

    [McpServerTool(Name = "run_code_foundry")]
    [Description("Generate, compile, and (Run) execute in-process or (Deploy) build+restart a new neuron. tier is 'Run' or 'Deploy'.")]
    public async Task<string> RunCodeFoundry(
        [Description("English spec of the code to generate")] string spec,
        [Description("'Run' for Tier-1 in-process, 'Deploy' for Tier-2 durable")] string tier = "Run",
        [Description("Apply automatically")] bool autoApply = true)
    {
        var parsedTier = string.Equals(tier, "Deploy", StringComparison.OrdinalIgnoreCase)
            ? TargetTier.Deploy
            : TargetTier.Run;

        var loop = grains.GetGrain<ICodeFoundryLoopNeuron>("foundry-main");
        await loop.FireAsync(new FoundryRequest(spec, parsedTier, autoApply));

        var timeline = await loop.GetOutgoingTimelineAsync();
        var terminal = timeline.LastOrDefault(s =>
            s.Type == nameof(FoundryCompleted) || s.Type == nameof(FoundryRolledBack));
        return terminal?.Type ?? "FoundryRequest accepted (no terminal synapse yet).";
    }

    [McpServerTool(Name = "ingest_company_source"), Description("Ingest company process playbook or transcript into the company brain context (for skill crystallization).")]
    public async Task<string> IngestCompanySource(
        [Description("Collection e.g. refunds")] string collection,
        [Description("Source identifier")] string sourceId,
        [Description("Raw text content of policy or transcript")] string text)
    {
        var ck = grains.GetGrain<ICompanyKnowledgeNeuron>("company-main");
        await ck.FireAsync(new IngestCompanySource(collection, sourceId, text));
        return $"Ingested {sourceId} into {collection}. Ready for crystallize/skill creation.";
    }

    [McpServerTool(Name = "invoke_company_skill"), Description("Fire a trigger to an embodied company skill (e.g. RefundRequested) and return the audit emissions from the journal.")]
    public async Task<string> InvokeCompanySkill(
        [Description("Skill name e.g. RefundHandling")] string skillName,
        [Description("Trigger type e.g. RefundRequested")] string triggerType,
        [Description("Simple payload key=value;key2=value2 for the trigger")] string payload)
    {
        var gen = grains.GetGrain<IGeneratedNeuron>($"skill-{skillName.ToLowerInvariant()}");
        // For demo use ExperienceUsed path which always works; typed would need full deserialze.
        await gen.FireAsync(new ExperienceUsed(skillName, $"{triggerType}:{payload}"));
        var tl = await gen.GetOutgoingTimelineAsync();
        var lastEmission = tl.OfType<PackEmission>().LastOrDefault();
        return lastEmission is not null
            ? $"Skill {skillName} emitted: {lastEmission.Output} (pack {lastEmission.Pack})"
            : "Skill invoked; check journal for PackEmission.";
    }

    [McpServerTool(Name = "create_company_skill"), Description("Run the full automated pipeline: ingest sources, crystallize process spec, synthesize IPackBehavior, publish+install via marketplace, verify execution and return result.")]
    public async Task<string> CreateCompanySkill([Description("Process name e.g. RefundHandling")] string processName)
    {
        var orchestrator = grains.GetGrain<ICompanySkillOrchestratorNeuron>("company-skill-main");
        await orchestrator.FireAsync(new CreateCompanySkill(processName));
        var tl = await orchestrator.GetOutgoingTimelineAsync();
        var result = tl.OfType<CompanySkillCreationResult>().LastOrDefault();
        return result is not null
            ? $"Create {result.ProcessName}@{result.Version}: Success={result.Success}. {result.Details}"
            : $"Create request for {processName} accepted. Check orchestrator timeline.";
    }
}

