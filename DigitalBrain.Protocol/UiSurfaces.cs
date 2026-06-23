namespace DigitalBrain.Protocol;

/// <summary>
/// Base for dynamic UI surfaces returned by installed INO experiences.
/// Clients (Flutter via sdk/, future Telegram, etc.) render these instead of hard-coded UI.
/// </summary>
[GenerateSerializer]
public record UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props) : Synapse(nameof(UiSurface), DateTimeOffset.UtcNow);

public static class UiSurfaceKinds
{
    public const string AuthButton = "auth-button";
    public const string List = "list";
    public const string Ide = "ide";
    public const string KernelTasks = "kernel-tasks";
    public const string ActivityGraph = "activity-graph";
    public const string TaskWindow = "task-window";
    public const string UserInput = "user-input";
    public const string MarketplaceList = "marketplace-list";
    public const string Timeline = "timeline";
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
    public static UiSurface KernelTasks() => new(
        UiSurfaceKinds.KernelTasks,
        WithCommon(
            surfaceId: "surface.kernel-tasks",
            emitter: "digitalbrain.kernel",
            title: "Kernel Tasks",
            layout: UiSurfaceLayouts.Panel,
            props: new Dictionary<string, object?>
            {
                ["tasks"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["taskId"] = "task-demo-1",
                        ["title"] = "Generate status summary",
                        ["state"] = "running",
                        ["progress"] = 0.35,
                        ["detail"] = "Reading recent neuron journals"
                    }
                },
                [UiSurfaceKeys.Actions] = new[]
                {
                    SynapseAction("inspect-task", "Inspect", nameof(RunKernelTask), new Dictionary<string, object?>
                    {
                        ["taskId"] = "task-demo-1",
                        ["description"] = "Inspect current task"
                    }),
                    SynapseAction("cancel-task", "Cancel", nameof(CancelKernelTask), new Dictionary<string, object?>
                    {
                        ["taskId"] = "task-demo-1"
                    })
                }
            }));

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
            emitter: "digitalbrain.kernel",
            title: "Task Window",
            layout: UiSurfaceLayouts.Panel,
            props: new Dictionary<string, object?>
            {
                ["taskId"] = "task-demo-1",
                ["state"] = "running",
                ["body"] = "Generate a concise status summary of the running kernel.",
                [UiSurfaceKeys.Actions] = new[]
                {
                    SynapseAction("cancel-task", "Cancel", nameof(CancelKernelTask), new Dictionary<string, object?>
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
                ["cancelAction"] = SynapseAction("dismiss-input", "Dismiss", nameof(CancelKernelTask), new Dictionary<string, object?>
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
                        ["type"] = nameof(KernelTaskStarted),
                        ["title"] = "Task started",
                        ["at"] = DateTimeOffset.UtcNow
                    }
                },
                ["filters"] = new Dictionary<string, object?>
                {
                    ["types"] = new[] { nameof(KernelTaskStarted), nameof(KernelTaskProgress), nameof(KernelTaskCompleted) }
                }
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
        int maxEvents = 20) =>
        new[]
        {
            KernelTasksFromTimelines(taskTimelines),
            ActivityGraphFromTimeline(graphTimeline, maxEvents),
            MarketplaceListFromPacks(publishedPacks, installedPacks),
            TimelineFromSynapses(timelineEvents, maxEvents)
        };

    public static UiSurface KernelTasksFromTimelines(
        IEnumerable<(string TaskId, IReadOnlyList<Synapse> Timeline)> taskTimelines)
    {
        var tasks = taskTimelines
            .Select(task => BuildKernelTask(task.TaskId, task.Timeline))
            .Where(task => task.Count > 0)
            .ToArray();

        return new UiSurface(
            UiSurfaceKinds.KernelTasks,
            WithCommon(
                surfaceId: "surface.kernel-tasks.live",
                emitter: "digitalbrain.kernel",
                title: "Kernel Tasks",
                layout: UiSurfaceLayouts.Panel,
                priority: 10,
                props: new Dictionary<string, object?>
                {
                    ["tasks"] = tasks,
                    [UiSurfaceKeys.Actions] = tasks
                        .SelectMany(task => TaskActions((string)task["taskId"]!, (string)task["title"]!))
                        .ToArray()
                }));
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

    private static Dictionary<string, object?> BuildKernelTask(string taskId, IReadOnlyList<Synapse> timeline)
    {
        var taskEvents = timeline
            .Where(s => IsTaskEventFor(s, taskId))
            .OrderBy(s => s.Timestamp)
            .ToList();

        if (taskEvents.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["taskId"] = taskId,
                ["title"] = taskId,
                ["state"] = "unknown",
                ["progress"] = 0.0,
                ["detail"] = "No task journal entries found."
            };
        }

        var created = taskEvents.OfType<KernelTaskCreated>().LastOrDefault();
        var latest = taskEvents.Last();
        var state = latest switch
        {
            KernelTaskCompleted => "completed",
            KernelTaskCancelled => "cancelled",
            KernelTaskProgress => "running",
            KernelTaskStarted => "running",
            KernelTaskCreated => "created",
            _ => "unknown"
        };
        var progress = latest switch
        {
            KernelTaskCompleted => 1.0,
            KernelTaskCancelled => 0.0,
            KernelTaskProgress => 0.65,
            KernelTaskStarted => 0.35,
            KernelTaskCreated => 0.1,
            _ => 0.0
        };
        var detail = latest switch
        {
            KernelTaskCompleted completed => completed.Result ?? "Completed",
            KernelTaskCancelled => "Cancelled",
            KernelTaskProgress taskProgress => taskProgress.Detail,
            KernelTaskStarted => "Started",
            KernelTaskCreated taskCreated => taskCreated.Description,
            _ => latest.Type
        };

        return new Dictionary<string, object?>
        {
            ["taskId"] = taskId,
            ["title"] = created?.Description ?? taskId,
            ["state"] = state,
            ["progress"] = progress,
            ["detail"] = detail,
            ["updatedAt"] = latest.Timestamp,
            [UiSurfaceKeys.Actions] = TaskActions(taskId, created?.Description ?? taskId)
        };
    }

    private static IReadOnlyDictionary<string, object?>[] TaskActions(string taskId, string description) =>
        new[]
        {
            UiSurfaceSamples.SynapseAction(
                "inspect-" + taskId,
                "Inspect",
                nameof(RunKernelTask),
                new Dictionary<string, object?>
                {
                    ["taskId"] = taskId,
                    ["description"] = "Inspect task: " + description
                }),
            UiSurfaceSamples.SynapseAction(
                "cancel-" + taskId,
                "Cancel",
                nameof(CancelKernelTask),
                new Dictionary<string, object?>
                {
                    ["taskId"] = taskId
                })
        };

    private static bool IsTaskEventFor(Synapse synapse, string taskId) =>
        synapse switch
        {
            KernelTaskCreated task => task.TaskId == taskId,
            KernelTaskStarted task => task.TaskId == taskId,
            KernelTaskProgress task => task.TaskId == taskId,
            KernelTaskCompleted task => task.TaskId == taskId,
            KernelTaskCancelled task => task.TaskId == taskId,
            _ => false
        };

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
            KernelTaskCreated task => task.Description,
            KernelTaskProgress task => task.Detail,
            KernelTaskCompleted task => task.Result ?? "Task completed",
            KernelTaskCancelled task => "Task cancelled: " + task.TaskId,
            PublishedList list => $"{list.Packs.Count} published packs",
            NeuroPackInstalled installed => "Installed " + installed.Pack.Name,
            ClusterActivity activity => $"{activity.NodeId}: {activity.Activity}",
            ThreeDGraphUpdate update => "Graph update: " + update.GraphId,
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
