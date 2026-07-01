using DigitalBrain.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

// Read-only DigitalBrain MCP tools: observe cluster state without side effects. Safe to expose over the
// kernel's HTTP transport (remotely reachable). Mutation tools live in DigitalBrainMutationTools (stdio-only).
[McpServerToolType]
public sealed class DigitalBrainReadTools(IGrainFactory grains) : DigitalBrainToolsBase(grains)
{
    [McpServerTool(Name = "ping_digitalbrain"), Description("Simple ping tool to verify MCP connection to DigitalBrain server works. Always returns success.")]
    public static string PingDigitalBrain() => "DigitalBrain MCP connected successfully. Cluster interaction tools ready when silo is running.";

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

    [McpServerTool(Name = "get_workbench_surfaces"), Description("Return dynamic UiSurface JSON for the Flutter workbench, derived from task, graph, marketplace, and timeline journals. Pass comma-separated taskIds when the caller knows active kernel tasks.")]
    public async Task<string> GetWorkbenchSurfaces(
        [Description("Comma-separated kernel task ids to include, if known.")] string taskIds = "",
        [Description("Max graph/timeline events to include")] int maxEvents = 20)
    {
        var taskTimelines = new List<(string TaskId, IReadOnlyList<Synapse> Timeline)>();
        foreach (var taskId in SplitIds(taskIds))
        {
            var task = Grains.GetGrain<INeuron>(taskId);
            taskTimelines.Add((taskId, await task.GetTimelineAsync()));
        }

        var graphTimeline = await ResolveNeuron("cluster-vis").GetTimelineAsync();
        var chartTimeline = await Grains.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();

        var marketplace = Grains.GetGrain<IMarketplaceNeuron>("market-main");
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

    [McpServerTool(Name = "list_marketplace"), Description("List currently published packs from the marketplace.")]
    public async Task<string> ListMarketplace()
    {
        var market = Grains.GetGrain<IMarketplaceNeuron>("market-main");
        var packs = await GetPublishedPacksWithLocalSeedsAsync(market);
        if (packs.Count == 0) return "No packs published yet.";
        return string.Join("\n", packs.Select(p =>
            $"- {p.Name}@{p.Version} (owner: {p.OwnerId}, private: {p.IsPrivate}, comm: {p.CommissionRate:P0})"));
    }
}
