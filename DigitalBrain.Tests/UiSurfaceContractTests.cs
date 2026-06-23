using DigitalBrain.Protocol;
using DigitalBrain.Silo;

namespace DigitalBrain.Tests;

public class UiSurfaceContractTests
{
    public static TheoryData<UiSurface, string[]> PlannedSurfaces => new()
    {
        { UiSurfaceSamples.KernelTasks(), new[] { "tasks", UiSurfaceKeys.Actions } },
        { UiSurfaceSamples.ActivityGraph(), new[] { "nodes", "edges", "events" } },
        { UiSurfaceSamples.TaskWindow(), new[] { "taskId", "state", "body", UiSurfaceKeys.Actions } },
        { UiSurfaceSamples.UserInput(), new[] { "prompt", "schema", "submitAction", "cancelAction" } },
        { UiSurfaceSamples.MarketplaceList(), new[] { "packs", "installAction", "updateAction" } },
        { UiSurfaceSamples.Timeline(), new[] { "events", "filters" } },
        { UiSurfaceSamples.DataChart(), new[] { UiSurfaceKeys.ChartSpec, "data", "x", "y", "chartType" } }
    };

    [Theory]
    [MemberData(nameof(PlannedSurfaces))]
    public void Planned_Surface_Samples_Carry_Common_Metadata(UiSurface surface, string[] requiredProps)
    {
        Assert.NotEmpty(surface.Kind);
        AssertCommonProp(surface, UiSurfaceKeys.SurfaceId);
        AssertCommonProp(surface, UiSurfaceKeys.Emitter);
        AssertCommonProp(surface, UiSurfaceKeys.Title);
        AssertCommonProp(surface, UiSurfaceKeys.Priority);
        AssertCommonProp(surface, UiSurfaceKeys.RequiresInput);
        AssertCommonProp(surface, UiSurfaceKeys.Layout);

        foreach (var prop in requiredProps)
        {
            Assert.True(surface.Props.ContainsKey(prop), $"{surface.Kind} is missing required prop '{prop}'.");
        }
    }

    [Fact]
    public void Planned_Surface_Kinds_Use_Stable_Kebab_Case_Names()
    {
        Assert.Equal("kernel-tasks", UiSurfaceKinds.KernelTasks);
        Assert.Equal("activity-graph", UiSurfaceKinds.ActivityGraph);
        Assert.Equal("task-window", UiSurfaceKinds.TaskWindow);
        Assert.Equal("user-input", UiSurfaceKinds.UserInput);
        Assert.Equal("marketplace-list", UiSurfaceKinds.MarketplaceList);
        Assert.Equal("timeline", UiSurfaceKinds.Timeline);
        Assert.Equal("data-chart", UiSurfaceKinds.DataChart);
    }

    [Fact]
    public void Action_Descriptors_Point_To_Existing_Synapse_Types()
    {
        var kernelActions = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            UiSurfaceSamples.KernelTasks().Props[UiSurfaceKeys.Actions]);
        Assert.Contains(kernelActions, a => Equals(a[UiSurfaceKeys.SynapseType], nameof(RunKernelTask)));
        Assert.Contains(kernelActions, a => Equals(a[UiSurfaceKeys.SynapseType], nameof(CancelKernelTask)));

        var userInput = UiSurfaceSamples.UserInput();
        AssertSynapseAction(userInput.Props["submitAction"], nameof(InoRequest));
        AssertSynapseAction(userInput.Props["cancelAction"], nameof(CancelKernelTask));

        var marketplace = UiSurfaceSamples.MarketplaceList();
        AssertSynapseAction(marketplace.Props["installAction"], nameof(InstallFromMarketplace));
        AssertSynapseAction(marketplace.Props["updateAction"], nameof(InstallFromMarketplace));
    }

    [Fact]
    public void Live_Kernel_Task_Surface_Is_Derived_From_Task_Journals()
    {
        var surface = UiSurfaceLiveData.KernelTasksFromTimelines(new[]
        {
            (
                "task-live-1",
                (IReadOnlyList<Synapse>)new Synapse[]
                {
                    new KernelTaskCreated("task-live-1", "Summarize running kernel"),
                    new KernelTaskStarted("task-live-1"),
                    new KernelTaskProgress("task-live-1", "Reading journals")
                })
        });

        var tasks = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["tasks"]);
        var task = Assert.Single(tasks);
        Assert.Equal("task-live-1", task["taskId"]);
        Assert.Equal("running", task["state"]);
        Assert.Equal("Reading journals", task["detail"]);

        var actions = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props[UiSurfaceKeys.Actions]);
        Assert.Contains(actions, a => Equals(a[UiSurfaceKeys.SynapseType], nameof(CancelKernelTask)));
    }

    [Fact]
    public void Live_Activity_Graph_Surface_Is_Derived_From_Cluster_Journals()
    {
        var surface = UiSurfaceLiveData.ActivityGraphFromTimeline(new Synapse[]
        {
            new ClusterActivity("ino-main", "reasoning", 0.8),
            new ClusterActivity("market-main", "listing", 0.4),
            new ThreeDGraphUpdate("main", "{\"node\":\"ino-main\"}")
        });

        var nodes = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["nodes"]);
        Assert.Contains(nodes, n => Equals(n["id"], "ino-main"));
        Assert.Contains(nodes, n => Equals(n["id"], "market-main"));

        var events = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["events"]);
        Assert.Contains(events, e => Equals(e["type"], nameof(ThreeDGraphUpdate)));
    }

    [Fact]
    public void Live_Marketplace_Surface_Treats_Local_Ui_Packs_As_Preinstalled()
    {
        var surface = UiSurfaceLiveData.MarketplaceListFromPacks(
            new[]
            {
                new NeuroPack(
                    "DigitalBrain.UIKit.ForUI",
                    "0.1.0",
                    "digitalbraintech",
                    Description: "ForUI primitive pack")
            },
            Array.Empty<NeuroPack>());

        var packs = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(surface.Props["packs"]);
        var pack = Assert.Single(packs);
        Assert.Equal("DigitalBrain.UIKit.ForUI", pack["name"]);
        Assert.Equal(true, pack["installed"]);

        AssertSynapseAction(surface.Props["installAction"], nameof(InstallFromMarketplace));
        AssertSynapseAction(surface.Props["updateAction"], nameof(InstallFromMarketplace));
    }

    [Fact]
    public void Live_DataChart_Surface_Is_Derived_From_Chart_Journal()
    {
        var generated = new DataChartGenerated("req-1", UiSurfaceSamples.DataChart());

        var surfaces = UiSurfaceLiveData.ChartSurfacesFromTimeline(new Synapse[] { generated });

        var surface = Assert.Single(surfaces);
        Assert.Equal(UiSurfaceKinds.DataChart, surface.Kind);
        Assert.True(surface.Props.ContainsKey(UiSurfaceKeys.ChartSpec));
    }

    [Fact]
    public void DataVisualization_Builder_Produces_Generic_DataChart_Surface()
    {
        var surface = DataChartBuilder.BuildSurface(
            "req-builder",
            "chart-main",
            "show revenue trend over time",
            """
            [
              { "month": "Jan", "revenue": 10, "region": "EU" },
              { "month": "Feb", "revenue": 14, "region": "EU" }
            ]
            """);

        Assert.Equal(UiSurfaceKinds.DataChart, surface.Kind);
        var spec = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(surface.Props[UiSurfaceKeys.ChartSpec]);
        Assert.Equal("line", spec["chartType"]);
        Assert.Equal("month", spec["x"]);
        Assert.Equal("revenue", spec["y"]);
        Assert.Equal("region", spec["series"]);
    }

    [Fact]
    public void Local_Marketplace_Seeds_Include_Preinstalled_Ui_Packs()
    {
        var packs = MarketplaceSeeds.LocalUiPacks;

        Assert.Contains(packs, p => p.Name == "DigitalBrain.UIKit.ForUI");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.Workbench");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.Graph3D");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.CreatorSurfaces");
        Assert.Contains(packs, p => p.Name == "DigitalBrain.UI.AspireFlutter");
        Assert.All(packs, p => Assert.Equal("digitalbraintech", p.OwnerId));
    }

    private static void AssertCommonProp(UiSurface surface, string key) =>
        Assert.True(surface.Props.ContainsKey(key), $"{surface.Kind} is missing common prop '{key}'.");

    private static void AssertSynapseAction(object? value, string expectedSynapseType)
    {
        var action = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(value);
        Assert.NotEmpty((string)action[UiSurfaceKeys.ActionId]!);
        Assert.NotEmpty((string)action[UiSurfaceKeys.Label]!);
        Assert.Equal(expectedSynapseType, action[UiSurfaceKeys.SynapseType]);
        Assert.True(action.ContainsKey(UiSurfaceKeys.Props));
    }
}
