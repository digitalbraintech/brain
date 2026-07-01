using DigitalBrain.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

// Mutating DigitalBrain MCP tools: fire side-effecting synapses, spend LLM tokens, or change marketplace/cluster
// state. Registered on the stdio transport only (local/trusted); withheld from the kernel's HTTP transport
// pending a remote auth decision.
[McpServerToolType]
public sealed class DigitalBrainMutationTools(IGrainFactory grains) : DigitalBrainToolsBase(grains)
{
    [McpServerTool(Name = "ask_llm_neuron"), Description("Ask the LLM neuron (powered by local Qwen/Ollama) a question or prompt. Returns the response. Requires the cluster (silo + ollama) to be running.")]
    public async Task<string> AskLlmNeuron(
        [Description("The prompt or question to send to the LLM neuron")] string prompt,
        [Description("Optional preferred model, e.g. 'qwen2.5-coder:1.5b'")] string? preferredModel = null)
    {
        var llm = Grains.GetGrain<ILlmNeuron>("llm-main");
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

    [McpServerTool(Name = "ask_ino"), Description("Ask the INO AI assistant (uses ContextNeuron for smart management).")]
    public Task<string> AskIno([Description("Prompt for INO navigation/assistant")] string prompt)
        => Grains.GetGrain<IInoNeuron>("ino-main").AskAsync(prompt);

    [McpServerTool(Name = "ino_code_editor"), Description("Interact with the INOCodeEditor neuron for visual editing/running of pack code.")]
    public async Task<string> InoCodeEditor([Description("Editor ID")] string id, [Description("Code or command")] string code)
    {
        var editor = Grains.GetGrain<IInoCodeEditor>("ino-editor-main");
        await editor.FireAsync(new InoCodeEdit(id, code));
        return $"INOCodeEditor received edit for {id}. Run to execute.";
    }

    [McpServerTool(Name = "update_context_filter"), Description("Update ContextNeuron (e.g. when a UI filter changes so INO sees it).")]
    public async Task<string> UpdateContextFilter(
        [Description("Filter/view name")] string view,
        [Description("Filter key")] string filter,
        [Description("Value")] string val)
    {
        var context = Grains.GetGrain<INeuron>("context-main");
        await context.FireAsync(new ContextUpdate("filter:" + view, filter, val));
        await context.FireAsync(new FilterChanged(view, filter, val)); // notify for LLM awareness
        return $"Context+Filter updated for {view}. INO/Context now aware.";
    }

    [McpServerTool(Name = "db_example"), Description("Exercise the DbSupportNeuron (connect + typed query via synapses).")]
    public async Task<string> DbExample([Description("Connection name e.g. northwind")] string name, [Description("Query")] string query)
    {
        var db = Grains.GetGrain<IDbSupportNeuron>("db-main");
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
        var loop = Grains.GetGrain<IClosedLoopNeuron>("closedloop-main");
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

        var loop = Grains.GetGrain<ICodeFoundryLoopNeuron>("foundry-main");
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
        var ck = Grains.GetGrain<ICompanyKnowledgeNeuron>("company-main");
        await ck.FireAsync(new IngestCompanySource(collection, sourceId, text));
        return $"Ingested {sourceId} into {collection}. Ready for crystallize/skill creation.";
    }

    [McpServerTool(Name = "invoke_company_skill"), Description("Fire a trigger to an embodied company skill (e.g. RefundRequested) and return the audit emissions from the journal.")]
    public async Task<string> InvokeCompanySkill(
        [Description("Skill name e.g. RefundHandling")] string skillName,
        [Description("Trigger type e.g. RefundRequested")] string triggerType,
        [Description("Simple payload key=value;key2=value2 for the trigger")] string payload)
    {
        var gen = Grains.GetGrain<IGeneratedNeuron>($"skill-{skillName.ToLowerInvariant()}");
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
        var orchestrator = Grains.GetGrain<ICompanySkillOrchestratorNeuron>("company-skill-main");
        await orchestrator.FireAsync(new CreateCompanySkill(processName));
        var tl = await orchestrator.GetOutgoingTimelineAsync();
        var result = tl.OfType<CompanySkillCreationResult>().LastOrDefault();
        return result is not null
            ? $"Create {result.ProcessName}@{result.Version}: Success={result.Success}. {result.Details}"
            : $"Create request for {processName} accepted. Check orchestrator timeline.";
    }

    [McpServerTool(Name = "visualize_data"), Description("Infer a generic data-chart UiSurface from JSON rows and return the generated surface JSON. The Flutter UI renders this dynamically by UiSurface.kind.")]
    public async Task<string> VisualizeData(
        [Description("Prompt describing what chart the user wants")] string prompt,
        [Description("JSON array of row objects, or an object containing rows/data/items")] string dataJson,
        [Description("Optional chart hint: bar, line, area, scatter, or pie")] string? chartHint = null)
    {
        var requestId = "chart-" + Guid.NewGuid().ToString("N")[..10];
        var chart = Grains.GetGrain<IDataVisualizationNeuron>("chart-main");
        await chart.FireAsync(new VisualizeDataRequest(prompt, dataJson, chartHint, requestId));

        var timeline = await chart.GetTimelineAsync();
        var failed = timeline.OfType<DataChartFailed>().LastOrDefault(result => result.RequestId == requestId);
        if (failed is not null)
        {
            return $"Data chart generation failed: {failed.Reason}";
        }

        var generated = timeline.OfType<DataChartGenerated>().LastOrDefault(result => result.RequestId == requestId);
        return generated is null
            ? $"VisualizeDataRequest accepted as {requestId}, but no chart result was found yet."
            : JsonSerializer.Serialize(generated.Surface, SurfaceJsonOptions);
    }

    [McpServerTool(Name = "fire_ui_action"), Description("Execute a UiSurface action descriptor by mapping synapseType and props to existing DigitalBrain command contracts.")]
    public async Task<string> FireUiAction(
        [Description("Action descriptor JSON with actionId, label, synapseType, and props")] string actionJson,
        [Description("Fallback neuron id for generic/demo actions")] string defaultNeuronId = "ino-main")
    {
        using var document = JsonDocument.Parse(actionJson);
        var action = document.RootElement;
        var synapseType = ReadString(action, UiSurfaceKeys.SynapseType);
        if (string.IsNullOrWhiteSpace(synapseType))
        {
            return "Action descriptor missing synapseType.";
        }

        var props = ReadObject(action, UiSurfaceKeys.Props);

        switch (synapseType)
        {
            case "RunKernelTask":
            {
                // UI action string kept as "RunKernelTask" for surface compat; message type is now the generic core protocol
                var taskId = ReadString(props, "taskId") ?? "task-" + Guid.NewGuid().ToString("N")[..8];
                var description = ReadString(props, "description") ?? ReadString(props, "prompt") ?? "Run task";
                await Grains.GetGrain<INeuron>(taskId).FireAsync(new RunTask(taskId, description));
                return $"Fired RunTask for {taskId}.";
            }
            case "CancelKernelTask":
            {
                var taskId = ReadString(props, "taskId");
                if (string.IsNullOrWhiteSpace(taskId)) return "CancelTask action requires props.taskId.";
                await Grains.GetGrain<INeuron>(taskId).FireAsync(new CancelTask(taskId));
                return $"Fired CancelTask for {taskId}.";
            }
            case nameof(InoRequest):
            {
                var prompt = ReadString(props, "prompt") ?? ReadString(props, "text");
                if (string.IsNullOrWhiteSpace(prompt)) return "InoRequest action requires props.prompt.";
                var sessionId = ReadString(props, "sessionId");
                await Grains.GetGrain<IInoNeuron>("ino-main").FireAsync(new InoRequest(prompt, sessionId));
                return "Fired InoRequest.";
            }
            case nameof(InstallFromMarketplace):
            {
                var packName = ReadString(props, "packName");
                var version = ReadString(props, "version") ?? "0.1.0";
                var buyerId = ReadString(props, "buyerId") ?? "current-user";
                if (string.IsNullOrWhiteSpace(packName)) return "InstallFromMarketplace action requires props.packName.";
                await Grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new InstallFromMarketplace(packName, version, buyerId));
                return $"Fired InstallFromMarketplace for {packName}@{version}.";
            }
            case nameof(ListPublished):
                await Grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new ListPublished());
                return "Fired ListPublished.";
            case nameof(RestartResource):
            {
                var resourceName = ReadString(props, "resourceName");
                if (string.IsNullOrWhiteSpace(resourceName)) return "RestartResource action requires props.resourceName.";
                await Grains.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new RestartResource(resourceName));
                return $"Fired RestartResource for {resourceName}.";
            }
            case nameof(ClosedLoopRequest):
            {
                var loopType = ReadString(props, "loopType") ?? "ui";
                var prompt = ReadString(props, "prompt") ?? "Run installed closed loop";
                await Grains.GetGrain<IClosedLoopNeuron>("closedloop-main").FireAsync(new ClosedLoopRequest(loopType, prompt));
                return $"Fired ClosedLoopRequest for {loopType}.";
            }
            default:
            {
                var target = ReadString(props, "neuronId") ?? defaultNeuronId;
                await ResolveNeuron(target).FireAsync(new DemoMessageSynapse(actionJson));
                return $"Forwarded unrecognized UI action '{synapseType}' to {target} as DemoMessageSynapse.";
            }
        }
    }

    [McpServerTool(Name = "publish_to_marketplace"), Description("Publish a pack/experience (e.g. generated neuron code) to the marketplace. Supports private and commission rate.")]
    public async Task<string> PublishToMarketplace(
        [Description("Pack name")] string packName,
        [Description("Version, e.g. '0.1-dev'")] string version,
        [Description("The code or content of the pack")] string code,
        [Description("Owner ID")] string ownerId = "mcp-user",
        [Description("Is private pack?")] bool isPrivate = false,
        [Description("Commission rate e.g. 0.15 for 15%")] double commissionRate = 0.15)
    {
        var market = Grains.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(packName, version, code, ownerId, isPrivate, commissionRate));
        return $"Published '{packName}@{version}' to marketplace (private={isPrivate}, commission={commissionRate:P0}).";
    }

    [McpServerTool(Name = "install_from_marketplace"), Description("Install a pack from the marketplace. Triggers commission.")]
    public async Task<string> InstallFromMarketplace(
        [Description("Pack name to install")] string packName,
        [Description("Version")] string version,
        [Description("Buyer ID for commission tracking")] string buyerId = "mcp-buyer")
    {
        var market = Grains.GetGrain<IMarketplaceNeuron>("market-main");
        await GetPublishedPacksWithLocalSeedsAsync(market);
        await market.FireAsync(new InstallFromMarketplace(packName, version, buyerId));
        return $"Installed '{packName}@{version}' for buyer '{buyerId}'. Commission should have been taken.";
    }
}
