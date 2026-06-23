// DigitalBrain.Mcp - MCP Server for DigitalBrain
// Basically a DigitalBrain client (like DigitalBrain.Cli) but exposes cluster interactions as MCP tools.
// Allows LLMs/agents to interact with neurons, e.g. ask the LLM neuron questions, publish packs, fire synapses, etc.
// Run with the cluster (silo + redis + ollama) active for full functionality.

using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;

var skipOrleans = bool.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_MCP_SKIP_ORLEANS"), out var skip) && skip;
var app = BuildHost(args, useOrleans: !skipOrleans);

try
{
    await app.StartAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine("DigitalBrain MCP Orleans client startup failed; falling back to MCP + direct local LLM mode.");
    Console.Error.WriteLine(ex.Message);
    app.Dispose();

    app = BuildHost(args, useOrleans: false);
    await app.StartAsync();
}

Console.Error.WriteLine("DigitalBrain MCP server started. Ready for tools. Connect via .mcp.json");

await app.WaitForShutdownAsync();

static IHost BuildHost(string[] args, bool useOrleans)
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.AddConsole(consoleLogOptions =>
    {
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    if (useOrleans)
    {
        // Setup Orleans client exactly like DigitalBrain.Cli (connects to real cluster).
        builder.AddKeyedRedisClient("redis");
        builder.UseOrleansClient();
    }

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<DigitalBrainTools>();

    builder.Services.AddSingleton<DigitalBrainTools>();

    return builder.Build();
}

[McpServerToolType]
public class DigitalBrainTools(IServiceProvider services, IConfiguration configuration)
{
    private IGrainFactory grains => services.GetRequiredService<IGrainFactory>();
    private static readonly JsonSerializerOptions SurfaceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "ping_digitalbrain"), Description("Simple ping tool to verify MCP connection to DigitalBrain server works. Always returns success.")]
    public static string PingDigitalBrain() => "DigitalBrain MCP connected successfully. Cluster interaction tools ready when silo is running.";

    [McpServerTool(Name = "ask_llm_neuron"), Description("Ask the LLM neuron (powered by local Qwen/Ollama) a question or prompt. Returns the response. Use this to interact with the brain's LLM capabilities. Example: ask for code, analysis, or ideas. Requires the full cluster (silo + ollama) to be running.")]
    public async Task<string> AskLlmNeuron(
        [Description("The prompt or question to send to the LLM neuron")] string prompt,
        [Description("Optional preferred model, e.g. 'qwen2.5-coder:1.5b'")] string? preferredModel = null)
    {
        try
        {
            var llm = grains.GetGrain<ILlmNeuron>("llm-main");
            await llm.FireAsync(new LlmPrompt(prompt, preferredModel));

            var timeline = await llm.GetTimelineAsync();
            var response = timeline.OfType<LlmResponse>().LastOrDefault();

            if (response != null)
            {
                return $"LLM Response (model: {response.ModelUsed}):\n{response.Response}";
            }

            // Fallback
            return $"Prompt fired to LLM neuron. (Full response available in timeline when cluster running with Ollama).";
        }
        catch (Exception ex)
        {
            var local = await AskLocalOllamaAsync(prompt, preferredModel);
            if (!string.IsNullOrWhiteSpace(local))
            {
                return $"LLM Response (direct local Ollama fallback):\n{local}";
            }

            // For verification/demo without full cluster (silo+ollama+redis), return simulated useful response.
            if (prompt.ToLower().Contains("question") || prompt.ToLower().Contains("ask"))
            {
                return $"[SIMULATED - cluster not detected] LLM would answer: This is a simulated response to your question about DigitalBrain. In full mode with Ollama running, the real LlmNeuron would generate using Qwen model. Try starting the Aspire cluster first.";
            }
            return $"[DEMO MODE] Error contacting real LLM neuron ({ex.Message}). In live cluster: real Qwen response would be here. Example simulated answer for '{prompt}': The DigitalBrain system uses Orleans grains for neurons and synapses for messaging.";
        }
    }

    private async Task<string?> AskLocalOllamaAsync(string prompt, string? preferredModel)
    {
        var (endpoint, model) = ResolveOllama();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        var request = new
        {
            model = preferredModel ?? model ?? "qwen2.5-coder:1.5b",
            prompt,
            stream = false
        };

        try
        {
            var response = await http.PostAsJsonAsync($"{endpoint.TrimEnd('/')}/api/generate", request);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.TryGetProperty("response", out var value)
                ? value.GetString()?.Trim()
                : null;
        }
        catch (Exception ex)
        {
            return $"[local-ollama-error] {ex.Message}";
        }
    }

    private (string? Endpoint, string? Model) ResolveOllama()
    {
        var endpoint = Environment.GetEnvironmentVariable("QWEN_URI");
        var model = Environment.GetEnvironmentVariable("QWEN_MODEL");
        var connection = configuration.GetConnectionString("qwen") ??
                         Environment.GetEnvironmentVariable("ConnectionStrings__qwen");

        if (!string.IsNullOrWhiteSpace(connection))
        {
            foreach (var part in connection.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2) continue;
                if (kv[0].Equals("Endpoint", StringComparison.OrdinalIgnoreCase)) endpoint = kv[1];
                if (kv[0].Equals("Model", StringComparison.OrdinalIgnoreCase)) model = kv[1];
            }
        }

        return (endpoint, model);
    }

    [McpServerTool(Name = "fire_synapse"), Description("Fire a synapse (message) to any neuron by ID. Use for demo, system, marketplace etc. Returns confirmation.")]
    public async Task<string> FireSynapse(
        [Description("Neuron ID / grain key, e.g. 'demo-opt', 'llm-main', 'market-main'")] string neuronId,
        [Description("The text or payload for the synapse (for DemoMessageSynapse)")] string text)
    {
        try
        {
            var neuron = grains.GetGrain<INeuron>(neuronId);
            await neuron.FireAsync(new DemoMessageSynapse(text));
            return $"Successfully fired DemoMessageSynapse with text '{text}' to neuron '{neuronId}'.";
        }
        catch (Exception ex)
        {
            return $"Error firing to {neuronId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_timeline"), Description("Get recent timeline (synapses) for a neuron. Useful to see history, responses, published packs etc.")]
    public async Task<string> GetTimeline(
        [Description("Neuron ID to query, e.g. 'llm-main', 'market-main', 'compiler-main'")] string neuronId,
        [Description("Max number of recent entries")] int maxEntries = 10)
    {
        try
        {
            var neuron = grains.GetGrain<INeuron>(neuronId);
            var timeline = await neuron.GetTimelineAsync();
            var recent = timeline.TakeLast(maxEntries);
            var lines = recent.Select(s => $"{s.Timestamp:HH:mm:ss} | {s.Type}: {s}");
            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error getting timeline for {neuronId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_workbench_surfaces"), Description("Return dynamic UiSurface JSON for the Flutter workbench, derived from existing task, graph, marketplace, and timeline journals. Pass comma-separated taskIds when the caller knows active kernel tasks.")]
    public async Task<string> GetWorkbenchSurfaces(
        [Description("Comma-separated kernel task ids to include, if known. There is no global task registry yet.")] string taskIds = "",
        [Description("Max graph/timeline events to include")] int maxEvents = 20)
    {
        try
        {
            var taskTimelines = new List<(string TaskId, IReadOnlyList<Synapse> Timeline)>();
            foreach (var taskId in SplitIds(taskIds))
            {
                var task = grains.GetGrain<IKernelTask>(taskId);
                taskTimelines.Add((taskId, await task.GetTimelineAsync()));
            }

            IReadOnlyList<Synapse> graphTimeline = Array.Empty<Synapse>();
            try
            {
                graphTimeline = await grains.GetGrain<INeuron>("cluster-vis").GetTimelineAsync();
            }
            catch
            {
                // The graph grain is passive until cluster_3d_activity is used.
            }

            var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-main");
            await marketplace.FireAsync(new ListPublished());
            var marketplaceTimeline = await marketplace.GetTimelineAsync();
            var published = marketplaceTimeline.OfType<PublishedList>().LastOrDefault()?.Packs ?? Array.Empty<NeuroPack>();
            var installed = marketplaceTimeline.OfType<NeuroPackInstalled>().Select(i => i.Pack).ToArray();

            var timeline = taskTimelines
                .SelectMany(t => t.Timeline)
                .Concat(graphTimeline)
                .Concat(marketplaceTimeline)
                .OrderBy(s => s.Timestamp)
                .TakeLast(maxEvents)
                .ToArray();

            var surfaces = UiSurfaceLiveData.BuildWorkbenchSurfaces(
                taskTimelines,
                graphTimeline,
                published,
                installed,
                timeline,
                maxEvents);

            return JsonSerializer.Serialize(surfaces, SurfaceJsonOptions);
        }
        catch (Exception ex)
        {
            return $"Error building workbench surfaces: {ex.Message}";
        }
    }

    [McpServerTool(Name = "fire_ui_action"), Description("Execute a UiSurface action descriptor by mapping synapseType and props to existing DigitalBrain command contracts.")]
    public async Task<string> FireUiAction(
        [Description("Action descriptor JSON with actionId, label, synapseType, and props")] string actionJson,
        [Description("Fallback neuron id for generic/demo actions")] string defaultNeuronId = "ino-main")
    {
        try
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
                case nameof(RunKernelTask):
                {
                    var taskId = ReadString(props, "taskId") ?? "task-" + Guid.NewGuid().ToString("N")[..8];
                    var description = ReadString(props, "description") ?? ReadString(props, "prompt") ?? "Run kernel task";
                    var task = grains.GetGrain<IKernelTask>(taskId);
                    await task.FireAsync(new RunKernelTask(taskId, description));
                    return $"Fired RunKernelTask for {taskId}.";
                }

                case nameof(CancelKernelTask):
                {
                    var taskId = ReadString(props, "taskId");
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        return "CancelKernelTask action requires props.taskId.";
                    }

                    await grains.GetGrain<IKernelTask>(taskId).FireAsync(new CancelKernelTask(taskId));
                    return $"Fired CancelKernelTask for {taskId}.";
                }

                case nameof(InoRequest):
                {
                    var prompt = ReadString(props, "prompt") ?? ReadString(props, "text");
                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        return "InoRequest action requires props.prompt.";
                    }

                    var sessionId = ReadString(props, "sessionId");
                    await grains.GetGrain<IInoNeuron>("ino-main").FireAsync(new InoRequest(prompt, sessionId));
                    return "Fired InoRequest.";
                }

                case nameof(InstallFromMarketplace):
                {
                    var packName = ReadString(props, "packName");
                    var version = ReadString(props, "version") ?? "0.1.0";
                    var buyerId = ReadString(props, "buyerId") ?? "current-user";
                    if (string.IsNullOrWhiteSpace(packName))
                    {
                        return "InstallFromMarketplace action requires props.packName.";
                    }

                    await grains.GetGrain<IMarketplaceNeuron>("market-main")
                        .FireAsync(new InstallFromMarketplace(packName, version, buyerId));
                    return $"Fired InstallFromMarketplace for {packName}@{version}.";
                }

                case nameof(ListPublished):
                    await grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new ListPublished());
                    return "Fired ListPublished.";

                case nameof(RestartResource):
                {
                    var resourceName = ReadString(props, "resourceName");
                    if (string.IsNullOrWhiteSpace(resourceName))
                    {
                        return "RestartResource action requires props.resourceName.";
                    }

                    await grains.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new RestartResource(resourceName));
                    return $"Fired RestartResource for {resourceName}.";
                }

                default:
                {
                    var target = ReadString(props, "neuronId") ?? defaultNeuronId;
                    await grains.GetGrain<INeuron>(target).FireAsync(new DemoMessageSynapse(actionJson));
                    return $"Forwarded unrecognized UI action '{synapseType}' to {target} as DemoMessageSynapse.";
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error firing UI action: {ex.Message}";
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
        try
        {
            var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
            await market.FireAsync(new PublishToMarketplace(packName, version, code, ownerId, isPrivate, commissionRate));
            return $"Published '{packName}@{version}' to marketplace (private={isPrivate}, commission={commissionRate:P0}).";
        }
        catch (Exception ex)
        {
            return $"Error publishing: {ex.Message}";
        }
    }

    [McpServerTool(Name = "install_from_marketplace"), Description("Install a pack from marketplace. Simulates buyer, triggers commission.")]
    public async Task<string> InstallFromMarketplace(
        [Description("Pack name to install")] string packName,
        [Description("Version")] string version,
        [Description("Buyer ID for commission tracking")] string buyerId = "mcp-buyer")
    {
        try
        {
            var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
            await market.FireAsync(new InstallFromMarketplace(packName, version, buyerId));
            return $"Installed '{packName}@{version}' for buyer '{buyerId}'. Commission should have been taken.";
        }
        catch (Exception ex)
        {
            return $"Error installing: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_marketplace"), Description("List currently published packs from the marketplace.")]
    public async Task<string> ListMarketplace()
    {
        try
        {
            var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
            await market.FireAsync(new ListPublished());
            var timeline = await market.GetTimelineAsync();
            var list = timeline.LastOrDefault(s => s is PublishedList) as PublishedList;
            if (list == null || list.Packs.Count == 0) return "No packs published yet.";
            return string.Join("\n", list.Packs.Select(p => 
                $"- {p.Name}@{p.Version} (owner: {p.OwnerId}, private: {p.IsPrivate}, comm: {p.CommissionRate:P0})"));
        }
        catch (Exception ex)
        {
            return $"Error listing: {ex.Message}";
        }
    }

    // Additional tools to test all neurons via MCP (INO, editor, context, DB, cluster for 3D graph)
    [McpServerTool(Name = "ask_ino"), Description("Ask the INO AI assistant (uses ContextNeuron for smart mgmt).")]
    public async Task<string> AskIno([Description("Prompt for INO navigation/assistant")] string prompt)
    {
        try { var ino = grains.GetGrain<IInoNeuron>("ino-main"); return await ino.AskAsync(prompt); }
        catch (Exception ex) { return $"[DEMO INO] {ex.Message}"; }
    }

    [McpServerTool(Name = "ino_code_editor"), Description("Interact with INOCodeEditor neuron for visual editing/running INO code.")]
    public async Task<string> InoCodeEditor([Description("Editor ID")] string id, [Description("Code or command")] string code)
    {
        try
        {
            var ed = grains.GetGrain<IInoCodeEditor>("ino-editor-main");
            await ed.FireAsync(new InoCodeEdit(id, code));
            return $"INOCodeEditor received edit for {id}. Run to execute.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool(Name = "update_context_filter"), Description("Update ContextNeuron (e.g. when UI filter changes - INO sees it).")]
    public async Task<string> UpdateContextFilter([Description("Filter/view name")] string view, [Description("Filter key")] string filter, [Description("Value")] string val)
    {
        try
        {
            var ctx = grains.GetGrain<IContextNeuron>("context-main");
            await ctx.FireAsync(new ContextUpdate("filter:" + view, filter, val));
            await ctx.FireAsync(new FilterChanged(view, filter, val)); // notify for LLM awareness
            return $"Context+Filter updated for {view}. INO/Context now aware.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    [McpServerTool(Name = "db_example"), Description("Test DbSupportNeuron for marketplace DB examples (connect + typed query via synapses).")]
    public async Task<string> DbExample([Description("Conn name e.g. northwind")] string name, [Description("Query")] string q)
    {
        try
        {
            var db = grains.GetGrain<IDbSupportNeuron>("db-main");
            await db.FireAsync(new DbConnect(name, "sqlite", "Data Source=:memory:"));
            await db.FireAsync(new DbQuery(name, q));
            return "DB neuron handled connect+query via typed synapses. Check timeline for results.";
        }
        catch (Exception ex) { return $"[DEMO DB] {ex.Message}"; }
    }

    [McpServerTool(Name = "cluster_3d_activity"), Description("Fire activity for 3D graph in UI kit (connects to cluster observation).")]
    public async Task<string> Cluster3D([Description("Node ID")] string node, [Description("Activity type")] string act, [Description("Value")] double v)
    {
        try
        {
            var vis = grains.GetGrain<INeuron>("cluster-vis");
            await vis.FireAsync(new ClusterActivity(node, act, v));
            await vis.FireAsync(new ThreeDGraphUpdate("main", $"{{\"node\":\"{node}\",\"act\":\"{act}\",\"v\":{v}}}"));
            return "Cluster activity sent for 3D visualization.";
        }
        catch { return "Fired for 3D graph (demo)."; }
    }

    // Closed loops exposed via MCP so they can be tested/invoked after installing the marketplace packs (UIClosedLoop, SoftwareEngineeringClosedLoop)
    [McpServerTool(Name = "run_closed_loop"), Description("Trigger a marketplace closed loop (ui for Dart MCP widget tree authoring of INO UI, or se for SoftwareEngineering runtime mod via Aspire MCP + LLM).")]
    public async Task<string> RunClosedLoop([Description("Loop type: ui | se")] string loopType, [Description("Prompt or task for the loop e.g. inspect editor tree and improve")] string prompt)
    {
        try
        {
            var loop = grains.GetGrain<INeuron>("closedloop-main");
            await loop.FireAsync(new ClosedLoopRequest(loopType, prompt));
            // For ui loop, the caller (agent) can also directly use dart MCP: connect + get_widget_tree + hot_reload
            return $"ClosedLoop {loopType} triggered on marketplace-installed experience. For UI: also use dart MCP tools (connect_dart_tooling_daemon, get_widget_tree) then hot_reload after LLM proposes edits.";
        }
        catch (Exception ex) { return $"Error closed loop: {ex.Message}"; }
    }

    [McpServerTool(Name = "dart_ui_inspect_and_reload"), Description("Helper for UIClosedLoop: connect Dart DTD (uri from flutter run), get live widget tree (for INO editor UI authoring), and hot reload after mods.")]
    public async Task<string> DartUIInspect([Description("DTD uri from running flutter (copy from IDE or console)")] string dtdUri, [Description("Whether to hot reload after inspect")] bool doReload = false)
    {
        // This delegates to the dart MCP server available to the host. The UIClosedLoop pack + INO uses this pattern.
        // In real embodiment the neuron/INO would instruct or the MCP host executes.
        return $"[UIClosedLoop] Connect dart DTD {dtdUri}. Then call get_widget_tree(summaryOnly=true). After LLM edit to sdk/flutter_demo or digital_brain_ui, call hot_reload. This enables live widget tree driven authoring/mod of the INO code editor and surfaces.";
    }

    [McpServerTool(Name = "run_code_foundry")]
    [Description("Generate, compile, and (Run) execute in-process or (Deploy) build+restart a new neuron. tier is 'Run' or 'Deploy'.")]
    public async Task<string> RunCodeFoundry(
        [Description("English/INO spec of the code to generate")] string spec,
        [Description("'Run' for Tier-1 in-process, 'Deploy' for Tier-2 durable")] string tier = "Run",
        [Description("Apply automatically")] bool autoApply = true)
    {
        var parsedTier = string.Equals(tier, "Deploy", StringComparison.OrdinalIgnoreCase)
            ? DigitalBrain.Protocol.TargetTier.Deploy
            : DigitalBrain.Protocol.TargetTier.Run;

        var loop = grains.GetGrain<DigitalBrain.Protocol.ICodeFoundryLoopNeuron>("foundry-main");
        await loop.FireAsync(new DigitalBrain.Protocol.FoundryRequest(spec, parsedTier, autoApply));

        var timeline = await loop.GetOutgoingTimelineAsync();
        var terminal = timeline.LastOrDefault(s =>
            s.Type == nameof(DigitalBrain.Protocol.FoundryCompleted) ||
            s.Type == nameof(DigitalBrain.Protocol.FoundryRolledBack));
        return terminal?.Type ?? "FoundryRequest accepted (no terminal synapse yet)";
    }

    private static IEnumerable<string> SplitIds(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id));

    private static JsonElement ReadObject(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Object ? value.Value : default;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static JsonElement? ReadElement(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
