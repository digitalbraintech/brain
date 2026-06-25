using DigitalBrain.Core;
using ModelContextProtocol.Server;
using Orleans;
using System.ComponentModel;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

public partial class DigitalBrainTools
{
    [McpServerTool(Name = "get_workbench_surfaces"), Description("Return dynamic UiSurface JSON for the Flutter workbench, derived from task, graph, marketplace, and timeline journals. Pass comma-separated taskIds when the caller knows active kernel tasks.")]
    public async Task<string> GetWorkbenchSurfaces(
        [Description("Comma-separated kernel task ids to include, if known.")] string taskIds = "",
        [Description("Max graph/timeline events to include")] int maxEvents = 20)
    {
        var taskTimelines = new List<(string TaskId, IReadOnlyList<Synapse> Timeline)>();
        foreach (var taskId in SplitIds(taskIds))
        {
            var task = grains.GetGrain<INeuron>(taskId);
            taskTimelines.Add((taskId, await task.GetTimelineAsync()));
        }

        var graphTimeline = await ResolveNeuron("cluster-vis").GetTimelineAsync();
        var chartTimeline = await grains.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();

        var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-main");
        var published = await GetPublishedPacksWithLocalSeedsAsync(marketplace);
        var marketplaceTimeline = await marketplace.GetTimelineAsync();
        var installed = marketplaceTimeline.OfType<NeuroPackInstalled>().Select(i => i.Pack).ToArray();

        var timeline = taskTimelines
            .SelectMany(t => t.Timeline)
            .Concat(graphTimeline)
            .Concat(chartTimeline)
            .Concat(marketplaceTimeline)
            .OrderBy(s => s.Timestamp)
            .TakeLast(maxEvents)
            .ToArray();

        var surfaces = UiSurfaceLiveData.BuildWorkbenchSurfaces(
            taskTimelines, graphTimeline, published, installed, timeline, maxEvents, chartTimeline);

        return JsonSerializer.Serialize(surfaces, SurfaceJsonOptions);
    }

    [McpServerTool(Name = "visualize_data"), Description("Infer a generic data-chart UiSurface from JSON rows and return the generated surface JSON. The Flutter UI renders this dynamically by UiSurface.kind.")]
    public async Task<string> VisualizeData(
        [Description("Prompt describing what chart the user wants")] string prompt,
        [Description("JSON array of row objects, or an object containing rows/data/items")] string dataJson,
        [Description("Optional chart hint: bar, line, area, scatter, or pie")] string? chartHint = null)
    {
        var requestId = "chart-" + Guid.NewGuid().ToString("N")[..10];
        var chart = grains.GetGrain<IDataVisualizationNeuron>("chart-main");
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
                var taskId = ReadString(props, "taskId") ?? "task-" + Guid.NewGuid().ToString("N")[..8];
                var description = ReadString(props, "description") ?? ReadString(props, "prompt") ?? "Run kernel task";
                await grains.GetGrain<INeuron>(taskId).FireAsync(new RunKernelTask(taskId, description));
                return $"Fired RunKernelTask for {taskId}.";
            }
            case "CancelKernelTask":
            {
                var taskId = ReadString(props, "taskId");
                if (string.IsNullOrWhiteSpace(taskId)) return "CancelKernelTask action requires props.taskId.";
                await grains.GetGrain<INeuron>(taskId).FireAsync(new CancelKernelTask(taskId));
                return $"Fired CancelKernelTask for {taskId}.";
            }
            case nameof(InoRequest):
            {
                var prompt = ReadString(props, "prompt") ?? ReadString(props, "text");
                if (string.IsNullOrWhiteSpace(prompt)) return "InoRequest action requires props.prompt.";
                var sessionId = ReadString(props, "sessionId");
                await grains.GetGrain<IInoNeuron>("ino-main").FireAsync(new InoRequest(prompt, sessionId));
                return "Fired InoRequest.";
            }
            case nameof(InstallFromMarketplace):
            {
                var packName = ReadString(props, "packName");
                var version = ReadString(props, "version") ?? "0.1.0";
                var buyerId = ReadString(props, "buyerId") ?? "current-user";
                if (string.IsNullOrWhiteSpace(packName)) return "InstallFromMarketplace action requires props.packName.";
                await grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new InstallFromMarketplace(packName, version, buyerId));
                return $"Fired InstallFromMarketplace for {packName}@{version}.";
            }
            case nameof(ListPublished):
                await grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new ListPublished());
                return "Fired ListPublished.";
            case nameof(RestartResource):
            {
                var resourceName = ReadString(props, "resourceName");
                if (string.IsNullOrWhiteSpace(resourceName)) return "RestartResource action requires props.resourceName.";
                await grains.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new RestartResource(resourceName));
                return $"Fired RestartResource for {resourceName}.";
            }
            case nameof(ClosedLoopRequest):
            {
                var loopType = ReadString(props, "loopType") ?? "ui";
                var prompt = ReadString(props, "prompt") ?? "Run installed closed loop";
                await grains.GetGrain<IClosedLoopNeuron>("closedloop-main").FireAsync(new ClosedLoopRequest(loopType, prompt));
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
        var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(packName, version, code, ownerId, isPrivate, commissionRate));
        return $"Published '{packName}@{version}' to marketplace (private={isPrivate}, commission={commissionRate:P0}).";
    }

    [McpServerTool(Name = "install_from_marketplace"), Description("Install a pack from the marketplace. Triggers commission.")]
    public async Task<string> InstallFromMarketplace(
        [Description("Pack name to install")] string packName,
        [Description("Version")] string version,
        [Description("Buyer ID for commission tracking")] string buyerId = "mcp-buyer")
    {
        var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
        await GetPublishedPacksWithLocalSeedsAsync(market);
        await market.FireAsync(new InstallFromMarketplace(packName, version, buyerId));
        return $"Installed '{packName}@{version}' for buyer '{buyerId}'. Commission should have been taken.";
    }

    [McpServerTool(Name = "list_marketplace"), Description("List currently published packs from the marketplace.")]
    public async Task<string> ListMarketplace()
    {
        var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
        var packs = await GetPublishedPacksWithLocalSeedsAsync(market);
        if (packs.Count == 0) return "No packs published yet.";
        return string.Join("\n", packs.Select(p =>
            $"- {p.Name}@{p.Version} (owner: {p.OwnerId}, private: {p.IsPrivate}, comm: {p.CommissionRate:P0})"));
    }
}

