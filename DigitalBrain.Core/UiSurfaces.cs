namespace DigitalBrain.Core;

using System.Text.Json.Nodes;

[GenerateSerializer]
public record UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props) : Synapse(nameof(UiSurface), DateTimeOffset.UtcNow)
{
    public const string RfwKind = "rfw";
    public const string WidgetTreeKind = "widget-tree";
    public const string AppShellKind = "app-shell";
    public const string ViewKind = "view";

    /// Creates a UiSurface carrying an RFW definition (unifies the previous separate RfwCard concept for UI purposes).
    public static UiSurface ForRfw(string libraryName, string rootWidget, string dataJson, string? source = null, string? emitter = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["libraryName"] = libraryName,
            ["rootWidget"] = rootWidget,
            ["dataJson"] = dataJson
        };
        if (source is not null) props["source"] = source;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;

        return new UiSurface(RfwKind, props);
    }

    /// Creates an RFW hop surface tagged so an experience host can recognize it and pick the
    /// active hop. The marker is merged INTO dataJson (the RFW bridge forwards Props["dataJson"]
    /// verbatim, so a top-level prop alone would never reach the Flutter client).
    public static UiSurface ForExperienceHop(
        string pack,
        string experienceId,
        string surfaceId,
        string libraryName,
        string rootWidget,
        string dataJson,
        string? title = null,
        string? emitter = null)
    {
        var experienceRef = $"{pack}/{experienceId}";
        var payload = JsonNode.Parse(dataJson) as JsonObject ?? new JsonObject();
        payload["activeExperience"] = experienceRef;
        payload["experienceId"] = experienceId;
        payload["surfaceId"] = surfaceId;

        var props = new Dictionary<string, object?>
        {
            ["libraryName"] = libraryName,
            ["rootWidget"] = rootWidget,
            ["dataJson"] = payload.ToJsonString(),
            ["activeExperience"] = experienceRef,
            ["experienceId"] = experienceId,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;

        return new UiSurface(RfwKind, props);
    }

    /// Creates a surface whose primary payload is a declarative widget tree (neurons author their own UI).
    /// The tree uses primitive names (e.g. "FSidebar", "FCard", "Panel") + children + bindings + actions.
    public static UiSurface ForWidgetTree(UiWidgetTree tree, string? title = null, string? emitter = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;

        return new UiSurface(WidgetTreeKind, props);
    }

    // Typed-tree sibling of ForExperienceHop: an experience hop whose payload is a UiWidgetTree of ui:* nodes.
    // Markers live in Props; UiSurfaceRfwBridge merges them into the wire dataJson and keys correlation on surfaceId.
    public static UiSurface ForExperienceHopTree(
        string pack,
        string experienceId,
        string surfaceId,
        UiWidgetTree tree,
        string? title = null,
        string? emitter = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            ["activeExperience"] = $"{pack}/{experienceId}",
            ["experienceId"] = experienceId,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;
        return new UiSurface(WidgetTreeKind, props);
    }
}

/// Declarative widget tree emitted by neurons inside UiSurface (WidgetTreeKind).
/// Uses NeuronUiKit (neuron:*) and forui:* names (forui:FScaffold, forui:FAutocomplete, forui:FSidebar etc) + rfw escapes.
/// Renderer maps to ForUI widgets and sends events back as synapses. Client is thin host.
[GenerateSerializer]
public record UiWidgetTree(
    [property: Id(0)] string Type, // "app-shell", NeuronUiKit.Menu, "forui:FScaffold", "forui:FAutocomplete", "list", "rfw", ...
    [property: Id(1)] IReadOnlyDictionary<string, object?> Props,
    [property: Id(2)] IReadOnlyList<UiWidgetTree>? Children = null,
    [property: Id(3)] string? RfwSource = null,
    [property: Id(4)] string? RfwRoot = null
);

/// Official Neuron UI Kit vocabulary (small, stable, server-driven only).
/// Neurons emit these as UiWidgetTree nodes inside app-shell / widget-tree surfaces.
/// Client renders; events carry targets or SynapseAction payloads back as UiInputSynapse.
public static class NeuronUiKit
{
    public const string Menu = "neuron:Menu";
    public const string MenuItem = "neuron:MenuItem";
    public const string ActionButton = "neuron:ActionButton";
    public const string NeuronButton = "neuron:NeuronButton";
    public const string NeuronList = "neuron:NeuronList";
    public const string NeuronListItem = "neuron:NeuronListItem";
    public const string Form = "neuron:Form";
    public const string Header = "neuron:Header";
    public const string Panel = "neuron:Panel";
    public const string Divider = "neuron:Divider";
    public const string Scaffold = "forui:FScaffold";
    public const string Sidebar = "forui:FSidebar";
    public const string Autocomplete = "forui:FAutocomplete";
    public const string TextField = "forui:FTextField";
    public const string Select = "forui:FSelect";
    public const string Notification = "forui:FNotification";
    public const string Toast = "forui:Toast";

    // Self-explanatory helpers for common tree nodes (used by emitters; keep emission sites small).
    public static UiWidgetTree BuildMenuItem(string label, string? targetSurfaceKind = null, IReadOnlyDictionary<string, object?>? action = null)
    {
        var p = new Dictionary<string, object?> { ["label"] = label };
        if (targetSurfaceKind is not null) p["targetSurfaceKind"] = targetSurfaceKind;
        if (action is not null) p["action"] = action;
        return new UiWidgetTree(MenuItem, p);
    }

    public static UiWidgetTree BuildHeader(string title, string? subtitle = null)
    {
        var p = new Dictionary<string, object?> { ["title"] = title };
        if (subtitle is not null) p["subtitle"] = subtitle;
        return new UiWidgetTree(Header, p);
    }

    public static UiWidgetTree BuildMenu(string title, IReadOnlyList<UiWidgetTree> items)
        => new(Menu, new Dictionary<string, object?> { ["title"] = title }, items);

    public static UiWidgetTree BuildSidebar(string title, IReadOnlyList<UiWidgetTree> items)
        => new(Sidebar, new Dictionary<string, object?> { ["title"] = title }, items);
}

// Curated UI-kit vocabulary (Slice 0). Each node is a thin ForUI cover on the client.
public static class Ui
{
    public const string Screen = "ui:Screen";
    public const string Text = "ui:Text";
    public const string TextField = "ui:TextField";
    public const string Button = "ui:Button";
    public const string Panel = "ui:Panel";
    public const string Checkbox = "ui:Checkbox";
    public const string Switch = "ui:Switch";
    public const string TextArea = "ui:TextArea";
    public const string Select = "ui:Select";
    public const string RadioGroup = "ui:RadioGroup";
    public const string Slider = "ui:Slider";
    public const string DateField = "ui:DateField";
    public const string Row = "ui:Row";
    public const string Column = "ui:Column";
    public const string Divider = "ui:Divider";
    public const string Header = "ui:Header";
    public const string Gap = "ui:Gap";
    public const string Heading = "ui:Heading";
    public const string Icon = "ui:Icon";
    public const string Avatar = "ui:Avatar";
    public const string Badge = "ui:Badge";
    public const string Tile = "ui:Tile";
    public const string List = "ui:List";
    public const string Tabs = "ui:Tabs";
    public const string Breadcrumb = "ui:Breadcrumb";
    public const string Pagination = "ui:Pagination";
    public const string Alert = "ui:Alert";
    public const string Progress = "ui:Progress";
    public const string Spinner = "ui:Spinner";
    public const string Tooltip = "ui:Tooltip";
    public const string Sidebar = "ui:Sidebar";
    public const string BottomNav = "ui:BottomNav";
    public const string Dialog = "ui:Dialog";
    public const string Sheet = "ui:Sheet";
    public const string Toast = "ui:Toast";
}

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

// Rich grammar-of-graphics spec for first-class interactive charts (maps directly to graphic package on client).
// Variables, marks, and selections are expressed as simple serializable structures.
[GenerateSerializer]
public record GraphicSpec(
    [property: Id(0)] string Title,
    [property: Id(1)] IReadOnlyList<IReadOnlyDictionary<string, object?>> Data,
    [property: Id(2)] IReadOnlyDictionary<string, object?> Variables,
    [property: Id(3)] IReadOnlyList<IReadOnlyDictionary<string, object?>> Marks,
    [property: Id(4)] IReadOnlyDictionary<string, object?>? Selections = null,
    [property: Id(5)] string? Summary = null,
    [property: Id(6)] IReadOnlyDictionary<string, object?>? Annotations = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["title"] = Title,
        ["data"] = Data,
        ["variables"] = Variables,
        ["marks"] = Marks,
        ["selections"] = Selections,
        ["summary"] = Summary,
        ["annotations"] = Annotations
    };
}

public static class UiSurfaceKinds
{
    public const string AuthButton = "auth-button";
    public const string Login = "login";
    public const string List = "list";
    public const string Ide = "ide";
    public const string ActivityGraph = "activity-graph";
    public const string TaskWindow = "task-window";
    public const string TaskManager = "task-manager";
    public const string UserInput = "user-input";
    public const string MarketplaceList = "marketplace-list";
    public const string InstalledBundles = "installed-bundles";
    public const string Timeline = "timeline";
    public const string DataChart = "data-chart";
    // All UI is UiSurface based. These enable neurons to own chrome, nav and full main UI.
    public const string AppShell = "app-shell";        // main root chrome + nav + layout, streamed by a neuron
    public const string ShellChrome = "shell-chrome";
    public const string NavConfig = "nav-config";
    public const string ViewDefinition = "view-definition";
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

    public static UiSurface Login(
        string? error = null,
        string clientId = "flutter",
        string? defaultUsername = null,
        string? defaultPassword = null)
    {
        // A field carries an optional pre-filled `value` the client seeds its input with.
        static Dictionary<string, object?> Field(string name, string label, string kind, string? value) => new()
        {
            ["name"] = name,
            ["label"] = label,
            ["kind"] = kind,
            ["required"] = true,
            ["value"] = value ?? string.Empty
        };

        Dictionary<string, object?>[] Fields() => new[]
        {
            Field("username", "Username", "text", defaultUsername),
            Field("password", "Password", "password", defaultPassword)
        };

        return new(
            UiSurfaceKinds.Login,
            WithCommon(
                surfaceId: "surface.login.local",
                emitter: "session-main",
                title: "Sign In",
                layout: UiSurfaceLayouts.Panel,
                requiresInput: true,
                priority: 100,
                props: new Dictionary<string, object?>
                {
                    ["clientId"] = clientId,
                    ["mode"] = "local",
                    ["error"] = error,
                    ["fields"] = Fields(),
                    ["submitAction"] = SynapseAction(
                        "local-login",
                        "Sign in",
                        nameof(LoginRequest),
                        new Dictionary<string, object?>
                        {
                            ["clientId"] = clientId
                        }),
                    ["tree"] = new UiWidgetTree(
                        NeuronUiKit.Form,
                        new Dictionary<string, object?>
                        {
                            ["title"] = "Sign In",
                            ["submitLabel"] = "Sign in",
                            ["error"] = error,
                            [UiSurfaceKeys.SynapseType] = nameof(LoginRequest),
                            ["clientId"] = clientId,
                            ["fields"] = Fields()
                        })
                }));
    }

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
                    ["buyerId"] = "anonymous",
                    ["userId"] = "anonymous"
                }),
                ["updateAction"] = SynapseAction("update-pack", "Update", nameof(InstallFromMarketplace), new Dictionary<string, object?>
                {
                    ["version"] = "0.1.0",
                    ["buyerId"] = "anonymous",
                    ["userId"] = "anonymous"
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

    public static UiSurface TaskManager() => new(
        UiSurfaceKinds.TaskManager,
        WithCommon(
            surfaceId: "surface.task-manager.demo",
            emitter: "kernel",
            title: "Task Manager",
            layout: UiSurfaceLayouts.Panel,
            priority: 20,
            props: new Dictionary<string, object?>
            {
                ["totals"] = new Dictionary<string, object?> { ["active"] = 1, ["completed"] = 2, ["failed"] = 0 },
                ["tasks"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["correlationId"] = "t1",
                        ["shortHash"] = "abc123",
                        ["originNeuron"] = "demo",
                        ["originIcon"] = "task",
                        ["ageMs"] = 1234,
                        ["edgeCount"] = 3,
                        ["status"] = "running"
                    }
                }
            }));

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

    public static UiSurface Chart(string surfaceId, string emitter, GraphicSpec spec) => new(
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
                ["graphicSpec"] = spec.ToProps(),
                ["data"] = spec.Data,
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
        IReadOnlyList<Synapse>? chartTimeline = null,
        string userId = "anonymous",
        string? sessionId = null)
    {
        userId = EffectiveUserId(userId);
        // Universal surfaces only. Kernel-specific surfaces (dashboard, tasks, rolling) are emitted by kernel-owned code.
        var surfaces = new List<UiSurface>
        {
            InstalledBundlesFromPacks(publishedPacks, installedPacks, userId, sessionId),
            ActivityGraphFromTimeline(graphTimeline, maxEvents),
            MarketplaceListFromPacks(publishedPacks, installedPacks, userId, sessionId),
            TaskManagerFromTasks(taskTimelines.SelectMany(t => t.Timeline).ToList(), maxEvents, userId, sessionId)
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
        IReadOnlyList<NeuroPack> installedPacks,
        string userId = "anonymous",
        string? sessionId = null)
    {
        userId = EffectiveUserId(userId);
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
                ["installed"] = installedKeys.Contains(PackKey(pack)) || IsPreinstalledLocalPack(pack),
                ["tier"] = pack.Manifest?.Tier.ToString(),
                ["channels"] = pack.Manifest?.Channels.Select(c => c.ToString()).ToArray(),
                ["entryExperienceId"] = pack.Manifest?.EntryExperience?.ExperienceId
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
                    ["userId"] = userId,
                    ["sessionId"] = sessionId,
                    ["packs"] = packs,
                    ["installAction"] = UiSurfaceSamples.SynapseAction(
                        "install-pack",
                        "Install",
                        nameof(InstallFromMarketplace),
                        new Dictionary<string, object?>
                        {
                            ["buyerId"] = userId,
                            ["userId"] = userId,
                            ["sessionId"] = sessionId
                        }),
                    ["updateAction"] = UiSurfaceSamples.SynapseAction(
                        "update-pack",
                        "Update",
                        nameof(InstallFromMarketplace),
                        new Dictionary<string, object?>
                        {
                            ["buyerId"] = userId,
                            ["userId"] = userId,
                            ["sessionId"] = sessionId
                        })
                }));
    }

    public static UiSurface MarketplaceTreeSurface(
        IReadOnlyList<NeuroPack> published,
        IReadOnlyList<NeuroPack> installed,
        string? tierFilter,
        string? channelFilter,
        string emitter,
        string title = "Marketplace",
        string userId = "anonymous",
        string? sessionId = null)
    {
        // Reuse the existing per-pack projection (name/version/description/installed/tier/channels/entryExperienceId).
        var listSurface = MarketplaceListFromPacks(published, installed, userId, sessionId);
        var allItems = (Dictionary<string, object?>[])listSurface.Props["packs"]!;

        bool Matches(Dictionary<string, object?> item)
        {
            if (tierFilter is not null && item.GetValueOrDefault("tier")?.ToString() != tierFilter) return false;
            if (channelFilter is not null)
            {
                var channels = item.GetValueOrDefault("channels") as string[] ?? Array.Empty<string>();
                if (!channels.Contains(channelFilter)) return false;
            }
            return true;
        }

        var filtered = allItems.Where(Matches).ToArray();

        var tiers = allItems
            .Select(i => i.GetValueOrDefault("tier")?.ToString())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();
        var channelNames = allItems
            .SelectMany(i => i.GetValueOrDefault("channels") as string[] ?? Array.Empty<string>())
            .Distinct()
            .ToList();

        UiWidgetTree FacetButton(string label, string? tier, string? channel) =>
            new(NeuronUiKit.ActionButton, new Dictionary<string, object?>
            {
                [UiSurfaceKeys.Label] = label,
                [UiSurfaceKeys.SynapseType] = nameof(FilterMarketplace),
                [UiSurfaceKeys.Props] = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["tier"] = tier, ["channel"] = channel }
            });

        var facetButtons = new List<UiWidgetTree> { FacetButton("All", null, null) };
        facetButtons.AddRange(tiers.Select(t => FacetButton(t!, t, null)));
        facetButtons.AddRange(channelNames.Select(c => FacetButton(c, null, c)));

        var tree = new UiWidgetTree("column", new Dictionary<string, object?>(), new List<UiWidgetTree>
        {
            new UiWidgetTree("row", new Dictionary<string, object?>(), facetButtons),
            new UiWidgetTree("list", new Dictionary<string, object?> { ["items"] = filtered })
        });

        return new UiSurface(UiSurfaceKinds.MarketplaceList, new Dictionary<string, object?>
        {
            ["tree"] = tree,
            ["packs"] = filtered,
            [UiSurfaceKeys.Title] = title,
            [UiSurfaceKeys.Emitter] = emitter,
            ["userId"] = userId,
            ["sessionId"] = sessionId,
            ["activeTier"] = tierFilter,
            ["activeChannel"] = channelFilter
        });
    }

    public static UiSurface InstalledBundlesFromPacks(
        IReadOnlyList<NeuroPack> publishedPacks,
        IReadOnlyList<NeuroPack> installedPacks,
        string userId = "anonymous",
        string? sessionId = null)
    {
        userId = EffectiveUserId(userId);
        var installedKeys = installedPacks
            .Select(PackKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bundles = installedPacks
            .Concat(publishedPacks.Where(pack =>
                installedKeys.Contains(PackKey(pack)) ||
                IsPreinstalledLocalPack(pack)))
            .GroupBy(PackKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BundleRow(group.First(), userId, sessionId))
            .ToArray();

        var experiences = bundles
            .SelectMany(bundle =>
                bundle.TryGetValue("experiences", out var value) &&
                value is IEnumerable<IReadOnlyDictionary<string, object?>> rows
                    ? rows
                    : Array.Empty<IReadOnlyDictionary<string, object?>>())
            .ToArray();

        var launcherTree = BuildInstalledLauncherTree(bundles, userId, sessionId);
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
                    ["userId"] = userId,
                    ["sessionId"] = sessionId,
                    ["bundles"] = bundles,
                    ["experiences"] = experiences,
                    ["tree"] = launcherTree
                }));
    }

    public static UiWidgetTree BuildInstalledLauncherTree(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> bundles,
        string userId = "anonymous",
        string? sessionId = null)
    {
        userId = EffectiveUserId(userId);
        var kids = new List<UiWidgetTree>();
        if (bundles != null)
        {
            foreach (var b in bundles)
            {
                var name = b.TryGetValue("name", out var n) ? n?.ToString() ?? "bundle" : "bundle";
                var ver = b.TryGetValue("version", out var v) ? v?.ToString() ?? "" : "";
                var owner = b.TryGetValue("ownerId", out var o) ? o?.ToString() ?? "" : "";
                var desc = b.TryGetValue("description", out var d) ? d?.ToString() ?? "" : "";
                var exps = (b.TryGetValue("experiences", out var e) && e is System.Collections.IEnumerable ie)
                    ? ie.OfType<IReadOnlyDictionary<string, object?>>().ToList()
                    : new List<IReadOnlyDictionary<string, object?>>();

                var actionKids = new List<UiWidgetTree>();
                // Generic "Open" maps to ExperienceUsed open on the embodiment. Skip it when an experience is
                // already labelled "Open" (e.g. ui-gallery) — otherwise the launcher shows two
                // "Open" buttons and the generic one's target (the pack name) doesn't reach the experience host.
                var hasOpenExperience = exps.Any(ex =>
                    ex.TryGetValue("name", out var exName) &&
                    string.Equals(exName?.ToString(), "Open", StringComparison.OrdinalIgnoreCase));
                if (!hasOpenExperience)
                {
                    actionKids.Add(new UiWidgetTree("fbutton", new Dictionary<string, object?>
                    {
                        ["label"] = "Open",
                        [UiSurfaceKeys.SynapseType] = nameof(ExperienceUsed),
                        ["packName"] = name,
                        ["action"] = "open",
                        ["targetSurfaceKind"] = name,
                        ["userId"] = userId,
                        ["sessionId"] = sessionId
                    }));
                }
                foreach (var ex in exps.Take(2))
                {
                    var lbl = ex.TryGetValue("name", out var ln) ? ln?.ToString() ?? "Run" : "Run";
                    IReadOnlyDictionary<string, object?>? act = null;
                    if (ex.TryGetValue("action", out var av) && av is IReadOnlyDictionary<string, object?> am) act = am;
                    var st = (act != null && act.TryGetValue(UiSurfaceKeys.SynapseType, out var stv)) ? stv?.ToString() ?? nameof(ExperienceUsed) : nameof(ExperienceUsed);
                    var pName = name;
                    var pAction = (act != null && act.TryGetValue(UiSurfaceKeys.Props, out var pv) && pv is IReadOnlyDictionary<string, object?> pmap && pmap.TryGetValue("action", out var av2)) ? av2?.ToString() ?? "run" : (ex.TryGetValue("experienceId", out var eid) ? eid?.ToString() ?? "run" : "run");
                    var btnP = act != null && act.TryGetValue(UiSurfaceKeys.Props, out var btnProps) && btnProps is IReadOnlyDictionary<string, object?> btnPropMap
                        ? new Dictionary<string, object?>(btnPropMap)
                        : new Dictionary<string, object?>();
                    btnP["label"] = lbl;
                    btnP[UiSurfaceKeys.SynapseType] = st;
                    btnP["packName"] = pName;
                    btnP["action"] = pAction;
                    btnP["userId"] = userId;
                    btnP["sessionId"] = sessionId;
                    if (act != null)
                    {
                        btnP["actionDescriptor"] = ScopedAction(act, userId, sessionId);
                    }
                    actionKids.Add(new UiWidgetTree("fbutton", btnP));
                }
                var row = new UiWidgetTree("row", new Dictionary<string, object?>(), actionKids);
                var cardKids = new List<UiWidgetTree>
                {
                    new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = desc }),
                    row
                };
                kids.Add(new UiWidgetTree("fcard", new Dictionary<string, object?>
                {
                    ["title"] = name + (string.IsNullOrEmpty(ver) ? "" : " " + ver),
                    ["subtitle"] = owner
                }, cardKids));
            }
        }
        return new UiWidgetTree("column", new Dictionary<string, object?>(), kids);
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

    public static UiSurface TaskManagerFromTasks(
        IReadOnlyList<Synapse> taskEvents,
        int maxEvents = 10,
        string userId = "anonymous",
        string? sessionId = null)
    {
        userId = EffectiveUserId(userId);
        var created = taskEvents.OfType<TaskCreated>().ToList();
        var progresses = taskEvents.OfType<TaskProgress>().ToList();
        var completed = taskEvents.OfType<TaskCompleted>().ToList();
        var cancelled = taskEvents.OfType<TaskCancelled>().ToList();

        int activeCount = Math.Max(0, created.Count - completed.Count - cancelled.Count);

        var taskRows = created.TakeLast(maxEvents).Select(c =>
        {
            var latest = progresses.LastOrDefault(p => p.TaskId == c.TaskId);
            string status = completed.Any(x => x.TaskId == c.TaskId) ? "completed"
                : cancelled.Any(x => x.TaskId == c.TaskId) ? "cancelled"
                : latest != null ? "running:" + latest.Detail : "created";

            var row = new Dictionary<string, object?>
            {
                ["taskId"] = c.TaskId.Value,
                ["correlationId"] = c.SynapseId,
                ["shortHash"] = c.TaskId.Value.Length > 8 ? c.TaskId.Value[..8] : c.TaskId.Value,
                ["originNeuron"] = c.Sender?.Value ?? "kernel",
                ["originIcon"] = "task",
                ["ageMs"] = (int)(DateTimeOffset.UtcNow - c.Timestamp).TotalMilliseconds,
                ["edgeCount"] = 1,
                ["status"] = status,
                ["userId"] = userId,
                ["sessionId"] = sessionId
            };
            if (!completed.Any(x => x.TaskId == c.TaskId) && !cancelled.Any(x => x.TaskId == c.TaskId))
            {
                row["cancelAction"] = UiSurfaceSamples.SynapseAction(
                    "cancel-task",
                    "Cancel",
                    nameof(CancelTask),
                    new Dictionary<string, object?>
                    {
                        ["taskId"] = c.TaskId.Value,
                        ["userId"] = userId,
                        ["sessionId"] = sessionId
                    });
            }

            return row;
        }).ToArray();

        var totals = new Dictionary<string, object?>
        {
            ["active"] = activeCount,
            ["completed"] = completed.Count,
            ["failed"] = 0
        };

        return new UiSurface(
            UiSurfaceKinds.TaskManager,
            WithCommon(
                surfaceId: "surface.task-manager.live",
                emitter: "kernel.task",
                title: "Task Manager",
                layout: UiSurfaceLayouts.Panel,
                priority: 20,
                props: new Dictionary<string, object?>
                {
                    ["userId"] = userId,
                    ["sessionId"] = sessionId,
                    ["totals"] = totals,
                    ["tasks"] = taskRows,
                    ["runAction"] = UiSurfaceSamples.SynapseAction(
                        "run-task",
                        "Run Task",
                        nameof(RunTask),
                        new Dictionary<string, object?>
                        {
                            ["userId"] = userId,
                            ["sessionId"] = sessionId
                        })
                }));
    }

    private static Dictionary<string, object?> BundleRow(NeuroPack pack, string userId, string? sessionId)
    {
        var experiences = ExperiencesForPack(pack, userId, sessionId).ToArray();
        return new Dictionary<string, object?>
        {
            ["name"] = pack.Name,
            ["version"] = pack.Version,
            ["ownerId"] = pack.OwnerId,
            ["userId"] = userId,
            ["sessionId"] = sessionId,
            ["installed"] = true,
            ["hasUi"] = true,
            ["status"] = experiences.Length == 0 ? "installed" : "ready",
            ["description"] = pack.Description,
            ["experienceCount"] = experiences.Length,
            ["scenarios"] = experiences.Select(e => e.TryGetValue("name", out var n) ? n?.ToString() : null).Where(s => s != null).ToArray(),
            ["experiences"] = experiences
        };
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ExperiencesForPack(NeuroPack pack, string userId, string? sessionId)
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
                    }),
                userId,
                sessionId);
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
                    }),
                userId,
                sessionId);
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
                    }),
                userId,
                sessionId);
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
                    }),
                userId,
                sessionId);
        }
        else if (pack.Name.Equals("DigitalBrain.Experience.GmailInsights", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "gmail-last-100-chart",
                "Gmail Insights",
                "experience",
                "Retrieve the last 100 Gmail messages, summarize them locally, and visualize message categories.",
                UiSurfaceSamples.SynapseAction(
                    "gmail-last-100-chart",
                    "Run",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?>
                    {
                        ["packName"] = pack.Name,
                        ["action"] = "gmail:last-100-chart"
                    }),
                userId,
                sessionId);
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
                    }),
                userId,
                sessionId);
        }
        // hello-world and ui-gallery demo branches deleted (Musk delete-first).
        else if (pack.Name.Contains("Dummy", StringComparison.OrdinalIgnoreCase) || pack.Name.Contains("DevPack", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "self-test",
                "Run self-test",
                "experience",
                "Execute pack self-test scenario.",
                UiSurfaceSamples.SynapseAction(
                    "dummy-self-test",
                    "Run self-test",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "self-test" }),
                userId,
                sessionId);
            yield return ExperienceRow(
                pack,
                "emit-test-surface",
                "Emit test surface",
                "app",
                "Pack responds by emitting a live UI surface for the main area.",
                UiSurfaceSamples.SynapseAction(
                    "dummy-emit-surface",
                    "Emit test surface",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "emit-test-surface" }),
                userId,
                sessionId);
        }
        else
        {
            // For newly created/authored packs: exercise real IPackBehavior via ExperienceUsed (distribution/install/embody/execution path).
            yield return ExperienceRow(
                pack,
                "run",
                "Run",
                "experience",
                "Execute the installed pack's Respond behavior.",
                UiSurfaceSamples.SynapseAction(
                    "run-" + ExperienceSlug(pack, "experience"),
                    "Run",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "run" }),
                userId,
                sessionId);
            yield return ExperienceRow(
                pack,
                "emit-test-surface",
                "Emit demo surface",
                "app",
                "Trigger pack scenario that emits a live UI surface into the main area.",
                UiSurfaceSamples.SynapseAction(
                    "emit-" + ExperienceSlug(pack, "surface"),
                    "Emit surface",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?> { ["packName"] = pack.Name, ["action"] = "emit-test-surface" }),
                userId,
                sessionId);
        }
    }

    private static IReadOnlyDictionary<string, object?> ExperienceRow(
        NeuroPack pack,
        string suffix,
        string name,
        string kind,
        string summary,
        IReadOnlyDictionary<string, object?> action,
        string userId,
        string? sessionId) => new Dictionary<string, object?>
        {
            ["experienceId"] = ExperienceSlug(pack, suffix),
            ["bundleName"] = pack.Name,
            ["userId"] = userId,
            ["sessionId"] = sessionId,
            ["name"] = name,
            ["kind"] = kind,
            ["status"] = "ready",
            ["summary"] = summary,
            ["action"] = ScopedAction(action, userId, sessionId)
        };

    private static string EffectiveUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();

    private static IReadOnlyDictionary<string, object?> ScopedAction(
        IReadOnlyDictionary<string, object?> action,
        string userId,
        string? sessionId)
    {
        var scopedAction = new Dictionary<string, object?>(action);
        var props = action.TryGetValue(UiSurfaceKeys.Props, out var value) &&
            value is IReadOnlyDictionary<string, object?> existingProps
                ? new Dictionary<string, object?>(existingProps)
                : new Dictionary<string, object?>();

        props["userId"] = EffectiveUserId(userId);
        props["sessionId"] = sessionId;

        if (string.Equals(action.TryGetValue(UiSurfaceKeys.SynapseType, out var type) ? type?.ToString() : null,
                nameof(InstallFromMarketplace),
                StringComparison.Ordinal) &&
            !props.ContainsKey("buyerId"))
        {
            props["buyerId"] = EffectiveUserId(userId);
        }

        scopedAction[UiSurfaceKeys.Props] = props;
        return scopedAction;
    }

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

    private static bool IsPreinstalledLocalPack(NeuroPack pack) =>
        pack.Name.StartsWith("DigitalBrain.UI", StringComparison.Ordinal) ||
        pack.Name.StartsWith("DigitalBrain.Experience", StringComparison.Ordinal) ||
        pack.Name.Equals("ui-gallery", StringComparison.OrdinalIgnoreCase) ||
        pack.Name.Contains("Dummy", StringComparison.OrdinalIgnoreCase);

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

// Kernel-owned surface kinds centralized here (with all other Ui kinds) per item 11.
// Kernel surfaces remain versioned with the kernel pack but names are single-sourced in Core.
public static class KernelUiSurfaceKinds
{
    public const string Dashboard = "kernel-dashboard";
    public const string Rolling = "kernel-rolling";
    public const string RollingDrain = "kernel-rolling-drain";
    public const string RollingVerify = "kernel-rolling-verify";
    public const string RollingComplete = "kernel-rolling-complete";
    public const string RollingRollback = "kernel-rolling-rollback";
}
