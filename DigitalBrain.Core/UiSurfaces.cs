namespace DigitalBrain.Core;

/// <summary>
/// Base for dynamic UI surfaces returned by installed INO experiences.
/// Clients (Flutter via sdk/, future Telegram, etc.) render these instead of hard-coded UI.
/// </summary>
[GenerateSerializer]
public record UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props) : Synapse(nameof(UiSurface), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ChartSpec(
    [property: Id(0)] string Title,
    [property: Id(1)] string ChartType,
    [property: Id(2)] IReadOnlyList<IReadOnlyDictionary<string, object?>> Data,
    [property: Id(3)] string X,
    [property: Id(4)] string Y,
    [property: Id(5)] string? Series = null,
    [property: Id(6)] string? Color = null,
    [property: Id(7)] bool Tooltip = true,
    [property: Id(8)] bool Crosshair = true,
    [property: Id(9)] string? Summary = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["title"] = Title,
        ["chartType"] = ChartType,
        ["data"] = Data,
        ["x"] = X,
        ["y"] = Y,
        ["series"] = Series,
        ["color"] = Color,
        ["tooltip"] = Tooltip,
        ["crosshair"] = Crosshair,
        ["summary"] = Summary
    };
}

public static class UiSurfaceKinds
{
    public const string AuthButton = "auth-button";
    public const string List = "list";
    public const string Ide = "ide";
    public const string ActivityGraph = "activity-graph";
    public const string TaskWindow = "task-window";
    public const string UserInput = "user-input";
    public const string MarketplaceList = "marketplace-list";
    public const string InstalledBundles = "installed-bundles";
    public const string Timeline = "timeline";
    public const string DataChart = "data-chart";
}

public static class UiSurfaceKeys
{
    public const string SurfaceId = "surfaceId";
    public const string Emitter = "emitter";
    public const string Title = "title";
    public const string Priority = "priority";
    public const string RequiresInput = "requiresInput";
    public const string Actions = "actions";
    public const string Layout = "layout";
    public const string ActionId = "actionId";
    public const string Label = "label";
    public const string SynapseType = "synapseType";
    public const string Props = "props";
    public const string ChartSpec = "chartSpec";
}

public static class UiSurfaceLayouts
{
    public const string Panel = "panel";
    public const string Inline = "inline";
    public const string Drawer = "drawer";
    public const string Modal = "modal";
    public const string Compact = "compact";
}

public static class UiSurfaceSamples
{
    public static UiSurface ActivityGraph() => new(
        UiSurfaceKinds.ActivityGraph,
        WithCommon(
            surfaceId: "surface.activity-graph",
            emitter: "digitalbrain.cluster",
            title: "Activity Graph",
            layout: UiSurfaceLayouts.Compact,
            props: new Dictionary<string, object?>
            {
                ["nodes"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "ino-main",
                        ["label"] = "INO",
                        ["activity"] = 0.8
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "market-main",
                        ["label"] = "Marketplace",
                        ["activity"] = 0.4
                    }
                },
                ["edges"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["from"] = "ino-main",
                        ["to"] = "market-main",
                        ["value"] = 0.3
                    }
                },
                ["events"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = nameof(ClusterActivity),
                        ["nodeId"] = "ino-main",
                        ["activity"] = "reasoning",
                        ["value"] = 0.8
                    }
                }
            }));

    public static UiSurface TaskWindow() => new(
        UiSurfaceKinds.TaskWindow,
        WithCommon(
            surfaceId: "surface.task-window.demo",
            emitter: "demo",
            title: "Task Window",
            layout: UiSurfaceLayouts.Panel,
            props: new Dictionary<string, object?>
            {
                ["taskId"] = "task-demo-1",
                ["state"] = "running",
                ["body"] = "Generate a concise status summary of current work.",
                [UiSurfaceKeys.Actions] = new[]
                {
                    SynapseAction("cancel-task", "Cancel", nameof(DemoMessageSynapse), new Dictionary<string, object?>
                    {
                        ["taskId"] = "task-demo-1"
                    })
                }
            }));

    public static UiSurface UserInput() => new(
        UiSurfaceKinds.UserInput,
        WithCommon(
            surfaceId: "surface.user-input.demo",
            emitter: "ino-main",
            title: "INO Input",
            layout: UiSurfaceLayouts.Modal,
            requiresInput: true,
            props: new Dictionary<string, object?>
            {
                ["prompt"] = "What should INO work on next?",
                ["schema"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["prompt"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["title"] = "Prompt"
                        }
                    },
                    ["required"] = new[] { "prompt" }
                },
                ["submitAction"] = SynapseAction("ask-ino", "Ask INO", nameof(InoRequest), new Dictionary<string, object?>
                {
                    ["sessionId"] = "workbench"
                }),
                ["cancelAction"] = SynapseAction("dismiss-input", "Dismiss", nameof(DemoMessageSynapse), new Dictionary<string, object?>
                {
                    ["taskId"] = "task-demo-1"
                })
            }));

    public static UiSurface MarketplaceList() => new(
        UiSurfaceKinds.MarketplaceList,
        WithCommon(
            surfaceId: "surface.marketplace-list",
            emitter: "market-main",
            title: "Marketplace",
            layout: UiSurfaceLayouts.Panel,
            props: new Dictionary<string, object?>
            {
                ["packs"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "DigitalBrain.UIKit.ForUI",
                        ["version"] = "0.1.0",
                        ["installed"] = true,
                        ["description"] = "Trusted ForUI primitive pack for DigitalBrain surfaces."
                    }
                },
                ["installAction"] = SynapseAction("install-pack", "Install", nameof(InstallFromMarketplace), new Dictionary<string, object?>
                {
                    ["version"] = "0.1.0",
                    ["buyerId"] = "current-user"
                }),
                ["updateAction"] = SynapseAction("update-pack", "Update", nameof(InstallFromMarketplace), new Dictionary<string, object?>
                {
                    ["version"] = "0.1.0",
                    ["buyerId"] = "current-user"
                })
            }));

    public static UiSurface InstalledBundles() => new(
        UiSurfaceKinds.InstalledBundles,
        WithCommon(
            surfaceId: "surface.installed-bundles",
            emitter: "market-main",
            title: "Installed Bundles",
            layout: UiSurfaceLayouts.Panel,
            priority: 11,
            props: new Dictionary<string, object?>
            {
                ["bundles"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "DigitalBrain.UI.Workbench",
                        ["version"] = "0.1.0",
                        ["ownerId"] = "digitalbraintech",
                        ["installed"] = true,
                        ["status"] = "ready",
                        ["description"] = "Startup workbench experience.",
                        ["experienceCount"] = 1,
                        ["experiences"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["experienceId"] = "digitalbrain-ui-workbench-open",
                                ["name"] = "Open Workbench",
                                ["kind"] = "app",
                                ["status"] = "ready",
                                ["summary"] = "Launch the main DigitalBrain workbench.",
                                ["action"] = SynapseAction(
                                    "open-workbench",
                                    "Open",
                                    nameof(InoRequest),
                                    new Dictionary<string, object?>
                                    {
                                        ["prompt"] = "Open the DigitalBrain workbench experience.",
                                        ["sessionId"] = "workbench"
                                    })
                            }
                        }
                    }
                },
                ["experiences"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["experienceId"] = "digitalbrain-ui-workbench-open",
                        ["bundleName"] = "DigitalBrain.UI.Workbench",
                        ["name"] = "Open Workbench",
                        ["kind"] = "app",
                        ["status"] = "ready"
                    }
                }
            }));

    public static UiSurface Timeline() => new(
        UiSurfaceKinds.Timeline,
        WithCommon(
            surfaceId: "surface.timeline",
            emitter: "digitalbrain.journal",
            title: "Timeline",
            layout: UiSurfaceLayouts.Drawer,
            props: new Dictionary<string, object?>
            {
                ["events"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = nameof(DemoMessageSynapse),
                        ["title"] = "Demo message",
                        ["at"] = DateTimeOffset.UtcNow
                    }
                },
                ["filters"] = new Dictionary<string, object?>
                {
                    ["types"] = new[] { nameof(DemoMessageSynapse), nameof(InoResponse), nameof(PublishedList) }
                }
            }));

    public static UiSurface DataChart() => DataChart(
        surfaceId: "surface.data-chart.demo",
        emitter: "chart-main",
        spec: new ChartSpec(
            Title: "Sales by Month",
            ChartType: "bar",
            Data: new[]
            {
                new Dictionary<string, object?> { ["month"] = "Jan", ["sales"] = 12 },
                new Dictionary<string, object?> { ["month"] = "Feb", ["sales"] = 18 }
            },
            X: "month",
            Y: "sales",
            Summary: "2 rows. Bar chart of sales by month."));

    public static UiSurface DataChart(string surfaceId, string emitter, ChartSpec spec) => new(
        UiSurfaceKinds.DataChart,
        WithCommon(
            surfaceId: surfaceId,
            emitter: emitter,
            title: spec.Title,
            layout: UiSurfaceLayouts.Panel,
            priority: 6,
            props: new Dictionary<string, object?>
            {
                [UiSurfaceKeys.ChartSpec] = spec.ToProps(),
                ["chartType"] = spec.ChartType,
                ["data"] = spec.Data,
                ["x"] = spec.X,
                ["y"] = spec.Y,
                ["series"] = spec.Series,
                ["color"] = spec.Color,
                ["tooltip"] = spec.Tooltip,
                ["crosshair"] = spec.Crosshair,
                ["summary"] = spec.Summary
            }));

    public static IReadOnlyDictionary<string, object?> SynapseAction(
        string actionId,
        string label,
        string synapseType,
        IReadOnlyDictionary<string, object?>? props = null) => new Dictionary<string, object?>
        {
            [UiSurfaceKeys.ActionId] = actionId,
            [UiSurfaceKeys.Label] = label,
            [UiSurfaceKeys.SynapseType] = synapseType,
            [UiSurfaceKeys.Props] = props ?? new Dictionary<string, object?>()
        };

    private static IReadOnlyDictionary<string, object?> WithCommon(
        string surfaceId,
        string emitter,
        string title,
        string layout,
        Dictionary<string, object?> props,
        int priority = 0,
        bool requiresInput = false)
    {
        props[UiSurfaceKeys.SurfaceId] = surfaceId;
        props[UiSurfaceKeys.Emitter] = emitter;
        props[UiSurfaceKeys.Title] = title;
        props[UiSurfaceKeys.Priority] = priority;
        props[UiSurfaceKeys.RequiresInput] = requiresInput;
        props[UiSurfaceKeys.Layout] = layout;

        if (!props.ContainsKey(UiSurfaceKeys.Actions))
        {
            props[UiSurfaceKeys.Actions] = Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        return props;
    }
}

public static class UiSurfaceLiveData
{
    public static IReadOnlyList<UiSurface> BuildWorkbenchSurfaces(
        IEnumerable<(string TaskId, IReadOnlyList<Synapse> Timeline)> taskTimelines,
        IReadOnlyList<Synapse> graphTimeline,
        IReadOnlyList<NeuroPack> publishedPacks,
        IReadOnlyList<NeuroPack> installedPacks,
        IReadOnlyList<Synapse> timelineEvents,
        int maxEvents = 20,
        IReadOnlyList<Synapse>? chartTimeline = null)
    {
        // Universal surfaces only. Kernel-specific surfaces (dashboard, tasks, rolling) are emitted by kernel-owned code.
        var surfaces = new List<UiSurface>
        {
            InstalledBundlesFromPacks(publishedPacks, installedPacks),
            ActivityGraphFromTimeline(graphTimeline, maxEvents),
            MarketplaceListFromPacks(publishedPacks, installedPacks)
        };

        surfaces.AddRange(ChartSurfacesFromTimeline(chartTimeline ?? timelineEvents, maxEvents));
        surfaces.Add(TimelineFromSynapses(timelineEvents, maxEvents));
        return surfaces;
    }

    public static UiSurface ActivityGraphFromTimeline(IReadOnlyList<Synapse> timeline, int maxEvents = 20)
    {
        var activity = timeline.OfType<ClusterActivity>().TakeLast(maxEvents).ToList();
        var nodes = activity
            .GroupBy(a => a.NodeId)
            .Select(g =>
            {
                var latest = g.Last();
                return new Dictionary<string, object?>
                {
                    ["id"] = latest.NodeId,
                    ["label"] = latest.NodeId,
                    ["activity"] = Math.Clamp(latest.Value, 0.0, 1.0)
                };
            })
            .ToArray();

        var edges = nodes
            .Zip(nodes.Skip(1), (from, to) => new Dictionary<string, object?>
            {
                ["from"] = from["id"],
                ["to"] = to["id"],
                ["value"] = 0.4
            })
            .ToArray();

        var events = timeline
            .Where(s => s is ClusterActivity or ThreeDGraphUpdate)
            .TakeLast(maxEvents)
            .Select(GraphEvent)
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.ActivityGraph,
            WithCommon(
                surfaceId: "surface.activity-graph.live",
                emitter: "digitalbrain.cluster",
                title: "Activity Graph",
                layout: UiSurfaceLayouts.Compact,
                priority: 5,
                props: new Dictionary<string, object?>
                {
                    ["nodes"] = nodes,
                    ["edges"] = edges,
                    ["events"] = events
                }));
    }

    public static UiSurface MarketplaceListFromPacks(
        IReadOnlyList<NeuroPack> publishedPacks,
        IReadOnlyList<NeuroPack> installedPacks)
    {
        var installedKeys = installedPacks
            .Select(PackKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var packs = publishedPacks
            .Select(pack => new Dictionary<string, object?>
            {
                ["name"] = pack.Name,
                ["version"] = pack.Version,
                ["ownerId"] = pack.OwnerId,
                ["private"] = pack.IsPrivate,
                ["commissionRate"] = pack.CommissionRate,
                ["description"] = pack.Description,
                ["installed"] = installedKeys.Contains(PackKey(pack)) || pack.Name.StartsWith("DigitalBrain.UI", StringComparison.Ordinal)
            })
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.MarketplaceList,
            WithCommon(
                surfaceId: "surface.marketplace-list.live",
                emitter: "market-main",
                title: "Marketplace",
                layout: UiSurfaceLayouts.Panel,
                priority: 4,
                props: new Dictionary<string, object?>
                {
                    ["packs"] = packs,
                    ["installAction"] = UiSurfaceSamples.SynapseAction(
                        "install-pack",
                        "Install",
                        nameof(InstallFromMarketplace),
                        new Dictionary<string, object?>
                        {
                            ["buyerId"] = "current-user"
                        }),
                    ["updateAction"] = UiSurfaceSamples.SynapseAction(
                        "update-pack",
                        "Update",
                        nameof(InstallFromMarketplace),
                        new Dictionary<string, object?>
                        {
                            ["buyerId"] = "current-user"
                        })
                }));
    }

    public static UiSurface InstalledBundlesFromPacks(
        IReadOnlyList<NeuroPack> publishedPacks,
        IReadOnlyList<NeuroPack> installedPacks)
    {
        var installedKeys = installedPacks
            .Select(PackKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bundles = installedPacks
            .Concat(publishedPacks.Where(pack =>
                installedKeys.Contains(PackKey(pack)) ||
                pack.Name.StartsWith("DigitalBrain.UI", StringComparison.Ordinal)))
            .GroupBy(PackKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BundleRow(group.First()))
            .ToArray();

        var experiences = bundles
            .SelectMany(bundle =>
                bundle.TryGetValue("experiences", out var value) &&
                value is IEnumerable<IReadOnlyDictionary<string, object?>> rows
                    ? rows
                    : Array.Empty<IReadOnlyDictionary<string, object?>>())
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.InstalledBundles,
            WithCommon(
                surfaceId: "surface.installed-bundles.live",
                emitter: "market-main",
                title: "Installed Bundles",
                layout: UiSurfaceLayouts.Panel,
                priority: 11,
                props: new Dictionary<string, object?>
                {
                    ["bundles"] = bundles,
                    ["experiences"] = experiences
                }));
    }

    public static UiSurface TimelineFromSynapses(IReadOnlyList<Synapse> timeline, int maxEvents = 20)
    {
        var events = timeline
            .OrderBy(s => s.Timestamp)
            .TakeLast(maxEvents)
            .Select(TimelineEvent)
            .ToArray();

        var filters = events
            .Select(e => e["type"])
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.Timeline,
            WithCommon(
                surfaceId: "surface.timeline.live",
                emitter: "digitalbrain.journal",
                title: "Timeline",
                layout: UiSurfaceLayouts.Drawer,
                priority: 2,
                props: new Dictionary<string, object?>
                {
                    ["events"] = events,
                    ["filters"] = new Dictionary<string, object?>
                    {
                        ["types"] = filters
                    }
                }));
    }

    public static IReadOnlyList<UiSurface> ChartSurfacesFromTimeline(IReadOnlyList<Synapse> timeline, int maxEvents = 20)
    {
        var generated = timeline
            .OfType<DataChartGenerated>()
            .Select(generated => generated.Surface);

        var direct = timeline
            .OfType<UiSurface>()
            .Where(surface => surface.Kind == UiSurfaceKinds.DataChart);

        return generated
            .Concat(direct)
            .TakeLast(maxEvents)
            .ToArray();
    }

    private static Dictionary<string, object?> BundleRow(NeuroPack pack)
    {
        var experiences = ExperiencesForPack(pack).ToArray();
        return new Dictionary<string, object?>
        {
            ["name"] = pack.Name,
            ["version"] = pack.Version,
            ["ownerId"] = pack.OwnerId,
            ["installed"] = true,
            ["status"] = experiences.Length == 0 ? "installed" : "ready",
            ["description"] = pack.Description,
            ["experienceCount"] = experiences.Length,
            ["experiences"] = experiences
        };
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ExperiencesForPack(NeuroPack pack)
    {
        if (pack.Name.Equals("DigitalBrain.UI.Workbench", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "open",
                "Open Workbench",
                "app",
                "Launch the main DigitalBrain workbench.",
                UiSurfaceSamples.SynapseAction(
                    "open-workbench",
                    "Open",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "Open the DigitalBrain workbench experience.",
                        ["sessionId"] = "workbench"
                    }));
        }
        else if (pack.Name.Equals("DigitalBrain.UI.Graph3D", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "cluster-graph",
                "Cluster Graph",
                "experience",
                "Open the live cluster graph experience.",
                UiSurfaceSamples.SynapseAction(
                    "open-cluster-graph",
                    "Open",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "Open the live cluster graph experience.",
                        ["sessionId"] = "workbench"
                    }));
        }
        else if (pack.Name.Equals("DigitalBrain.UI.CreatorSurfaces", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "create-surface",
                "Create Surface",
                "experience",
                "Start a generated UI surface workflow.",
                UiSurfaceSamples.SynapseAction(
                    "create-surface",
                    "Create",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "Create a new DigitalBrain UI surface from the installed CreatorSurfaces bundle.",
                        ["sessionId"] = "workbench"
                    }));
        }
        else if (pack.Name.Equals("DigitalBrain.UI.AspireFlutter", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "restart-ui",
                "Restart UI Client",
                "app",
                "Restart the Aspire-hosted Flutter UI client.",
                UiSurfaceSamples.SynapseAction(
                    "restart-flutter-ui",
                    "Restart",
                    nameof(RestartResource),
                    new Dictionary<string, object?>
                    {
                        ["resourceName"] = "flutter-ui"
                    }));
        }
        else if (pack.Name.Contains("ClosedLoop", StringComparison.OrdinalIgnoreCase))
        {
            var loopType = pack.Name.Contains("Software", StringComparison.OrdinalIgnoreCase) ? "se" : "ui";
            yield return ExperienceRow(
                pack,
                "run-closed-loop",
                "Run Closed Loop",
                "experience",
                "Run the installed closed-loop experience.",
                UiSurfaceSamples.SynapseAction(
                    "run-" + ExperienceSlug(pack, "closed-loop"),
                    "Run",
                    nameof(ClosedLoopRequest),
                    new Dictionary<string, object?>
                    {
                        ["loopType"] = loopType,
                        ["prompt"] = "Run installed bundle " + pack.Name
                    }));
        }
        else
        {
            yield return ExperienceRow(
                pack,
                "run",
                "Run Experience",
                "experience",
                "Run this installed bundle through INO.",
                UiSurfaceSamples.SynapseAction(
                    "run-" + ExperienceSlug(pack, "experience"),
                    "Run",
                    nameof(InoRequest),
                    new Dictionary<string, object?>
                    {
                        ["prompt"] = "Run installed bundle " + pack.Name,
                        ["sessionId"] = "workbench"
                    }));
        }
    }

    private static IReadOnlyDictionary<string, object?> ExperienceRow(
        NeuroPack pack,
        string suffix,
        string name,
        string kind,
        string summary,
        IReadOnlyDictionary<string, object?> action) => new Dictionary<string, object?>
        {
            ["experienceId"] = ExperienceSlug(pack, suffix),
            ["bundleName"] = pack.Name,
            ["name"] = name,
            ["kind"] = kind,
            ["status"] = "ready",
            ["summary"] = summary,
            ["action"] = action
        };

    private static string ExperienceSlug(NeuroPack pack, string suffix) =>
        (pack.Name + "-" + pack.Version + "-" + suffix)
            .ToLowerInvariant()
            .Replace(".", "-")
            .Replace("@", "-")
            .Replace(" ", "-");

    private static Dictionary<string, object?> GraphEvent(Synapse synapse) =>
        synapse switch
        {
            ClusterActivity activity => new Dictionary<string, object?>
            {
                ["type"] = nameof(ClusterActivity),
                ["nodeId"] = activity.NodeId,
                ["activity"] = activity.Activity,
                ["value"] = activity.Value,
                ["at"] = activity.Timestamp
            },
            ThreeDGraphUpdate update => new Dictionary<string, object?>
            {
                ["type"] = nameof(ThreeDGraphUpdate),
                ["graphId"] = update.GraphId,
                ["dataJson"] = update.DataJson,
                ["at"] = update.Timestamp
            },
            _ => TimelineEvent(synapse)
        };

    private static Dictionary<string, object?> TimelineEvent(Synapse synapse) =>
        new()
        {
            ["type"] = synapse.Type,
            ["title"] = TitleFor(synapse),
            ["at"] = synapse.Timestamp,
            ["sender"] = synapse.Sender?.Value,
            ["receiver"] = synapse.Receiver?.Value
        };

    private static string TitleFor(Synapse synapse) =>
        synapse switch
        {
            PublishedList list => $"{list.Packs.Count} published packs",
            NeuroPackInstalled installed => "Installed " + installed.Pack.Name,
            ClusterActivity activity => $"{activity.NodeId}: {activity.Activity}",
            ThreeDGraphUpdate update => "Graph update: " + update.GraphId,
            DataChartGenerated generated => "Chart generated: " + generated.RequestId,
            DataChartFailed failed => "Chart failed: " + failed.Reason,
            InoResponse response => response.Response,
            _ => synapse.Type
        };

    private static string PackKey(NeuroPack pack) => pack.Name + "@" + pack.Version;

    private static IReadOnlyDictionary<string, object?> WithCommon(
        string surfaceId,
        string emitter,
        string title,
        string layout,
        Dictionary<string, object?> props,
        int priority = 0,
        bool requiresInput = false)
    {
        props[UiSurfaceKeys.SurfaceId] = surfaceId;
        props[UiSurfaceKeys.Emitter] = emitter;
        props[UiSurfaceKeys.Title] = title;
        props[UiSurfaceKeys.Priority] = priority;
        props[UiSurfaceKeys.RequiresInput] = requiresInput;
        props[UiSurfaceKeys.Layout] = layout;

        if (!props.ContainsKey(UiSurfaceKeys.Actions))
        {
            props[UiSurfaceKeys.Actions] = Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        return props;
    }
}

/// <summary>
/// Auth button surface. GmailDigest etc. return this so the UI kit knows to show Google icon + wire OAuth.
/// </summary>
[GenerateSerializer]
public record AuthButtonSurface(
    string Provider,
    string Label,
    string Icon = "default",
    string Action = "oauth"
) : UiSurface(UiSurfaceKinds.AuthButton, new Dictionary<string, object?>
{
    ["provider"] = Provider,
    ["label"] = Label,
    ["icon"] = Icon,
    ["action"] = Action
});

/// <summary>
/// Simple list surface for tasks / marketplace items etc.
/// </summary>
[GenerateSerializer]
public record ListSurface(
    string Title,
    IReadOnlyList<string> Items
) : UiSurface(UiSurfaceKinds.List, new Dictionary<string, object?>
{
    ["title"] = Title,
    ["items"] = Items
});

/// <summary>
/// IDE / code edit surface for live INO modification + execute.
/// </summary>
[GenerateSerializer]
public record IdeSurface(
    string Title,
    string InitialCode,
    string Language = "ino"
) : UiSurface(UiSurfaceKinds.Ide, new Dictionary<string, object?>
{
    ["title"] = Title,
    ["code"] = InitialCode,
    ["language"] = Language
});
