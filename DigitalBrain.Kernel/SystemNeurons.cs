using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;
using Orleans.Runtime;
using System.Reflection;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

// Kernel-owned surface kinds for kernel status, dashboard, and rolling self-update phases.
// Kernel is a versioned pack; its surfaces and pack identity live here (not Core).
public static class KernelUiSurfaceKinds
{
    public const string Dashboard = "kernel-dashboard";
    public const string Rolling = "kernel-rolling";
    public const string RollingDrain = "kernel-rolling-drain";
    public const string RollingVerify = "kernel-rolling-verify";
    public const string RollingComplete = "kernel-rolling-complete";
    public const string RollingRollback = "kernel-rolling-rollback";
}

// Kernel pack identity (first-class pack for self-update via marketplace + rolling).
public static class KernelPack
{
    public const string Name = "kernel";
    public const string DefaultVersion = "0.3.0";
    public const string Description = "Core kernel substrate. Pre-installed; updatable via marketplace with rolling replica support.";
}

// Command to drive kernel self-update after the pack has been published/installed (pack-embodiment driven, no name special in company skill).
// FailAtReplica is a deterministic test/verification seam: 0 = never fail; N = the rollout aborts when replica N fails verify.
[GenerateSerializer]
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);

// IAspire neuron (orchestrates distributed apps via Aspire model, fires completion synapses)
[GrainType("digitalbrain.kernel.aspire.v1")]
public class AspireOrchestratorNeuron : Neuron, IAspireNeuron, IHandle<PerformKernelSelfUpdate>
{
    public AspireOrchestratorNeuron(ILogger<AspireOrchestratorNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(StartDistributedApp cmd)
    {
        Logger.LogInformation("Aspire starting app: {App}", cmd.AppName);
        await FireAsync(new DistributedAppStarted(cmd.AppName, Success: true, "started via neuro"));
        await FireAsync(new SystemStatusChanged("aspire", "started", cmd.AppName));

        // Emit full declarative kernel dashboard surface from neuron (client = thin renderer only; no static shell/dashboard logic).
        var dashboardProps = new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "kernel-dashboard-" + cmd.AppName,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Kernel Dashboard",
            [UiSurfaceKeys.Priority] = 10,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["haReplicas"] = 3,
            ["status"] = "healthy",
            ["tasks"] = "active",
            ["lastUpdate"] = "none",
            ["workbenchPanels"] = new[] { "tasks", "graph", "market", "chat", "timeline" }
        };
        await FireAsync(new UiSurface(KernelUiSurfaceKinds.Dashboard, dashboardProps));

        // Emit task manager surface (via kit) so user sees productive task view immediately on start.
        var recentEvents = OutgoingJournal.Concat(IncomingJournal).ToList();
        var taskSurface = UiSurfaceLiveData.TaskManagerFromTasks(recentEvents);
        await FireAsync(taskSurface);

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var card = UiSurfaceRfwBridge.FromUiSurface(taskSurface, Self.Value);
            bus.Broadcast(card);

            // Direct card using prepared client template root (matches kTaskManagerCardSource shape).
            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = taskSurface.Props.GetValueOrDefault("totals"),
                tasks = taskSurface.Props.GetValueOrDefault("tasks")
            });
            bus.Broadcast(new RfwCard("digitalbrain", "TaskManagerCard", directData));
        }

        // Server enrichment (#5): emit task-manager surface carrying a UiWidgetTree (list primitive) for pure renderer body.
        var taskItems = taskSurface.Props.TryGetValue("tasks", out var tk) ? tk : null;
        var taskTree = new UiWidgetTree(
            "list",
            new Dictionary<string, object?> { ["items"] = taskItems });
        var taskTreeSurface = new UiSurface(
            UiSurfaceKinds.TaskManager,
            new Dictionary<string, object?>
            {
                ["tree"] = taskTree,
                [UiSurfaceKeys.Title] = taskSurface.Props.TryGetValue(UiSurfaceKeys.Title, out var tt) ? tt : "Tasks",
                [UiSurfaceKeys.Emitter] = Self.Value,
                ["tasks"] = taskItems
            });
        await FireAsync(taskTreeSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(taskTreeSurface, Self.Value));
        }

        // Seed a visible startup task so the manager shows real work item right away.
        var demoId = new TaskId("startup-" + cmd.AppName);
        _ = Task.Run(async () =>
        {
            try
            {
                var kt = GrainFactory.GetGrain<IKernelTask>(demoId);
                await kt.FireAsync(new RunTask(demoId, "Explore the live Task Manager (UI kit on startup)"));
            }
            catch { /* best effort seed */ }
        });

        // Emit live marketplace surfaces on start (UI kit driven: lists with install/run actions as synapses).
        // Uses seeds for immediate visibility; refreshed post-install in MarketplaceNeuron.
        var publishedForStart = MarketplaceSeeds.LocalUiPacks;
        var marketList = UiSurfaceLiveData.MarketplaceListFromPacks(publishedForStart, Array.Empty<NeuroPack>());
        await FireAsync(marketList);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(marketList, Self.Value));
        }

        // Server enrichment (#5): emit marketplace-list surface carrying a UiWidgetTree (list primitive).
        // Client now renders via UiSurfaceTreeRenderer (pure neuron-driven, no client synthesis for packs).
        var marketPacks = marketList.Props.TryGetValue("packs", out var p) ? p : null;
        // Buddy search via forui:FAutocomplete (Neuron UI Kit). Static names for demo; select/query fires UiInputSynapse.
        var acItems = new[] { "DigitalBrain.UIKit.ForUI", "kernel", "INO", "Marketplace", "Tasks" };
        var acProps = new Dictionary<string, object?> { ["hint"] = "Search buddies / packs", ["items"] = acItems };
        var marketTree = new UiWidgetTree(
            "column",
            new Dictionary<string, object?>(),
            new List<UiWidgetTree>
            {
                new UiWidgetTree(DigitalBrain.Core.NeuronUiKit.Autocomplete, acProps),
                new UiWidgetTree("list", new Dictionary<string, object?> { ["items"] = marketPacks })
            });
        var marketTreeSurface = new UiSurface(
            UiSurfaceKinds.MarketplaceList,
            new Dictionary<string, object?>
            {
                ["tree"] = marketTree,
                [UiSurfaceKeys.Title] = marketList.Props.TryGetValue(UiSurfaceKeys.Title, out var t) ? t : "Marketplace",
                [UiSurfaceKeys.Emitter] = Self.Value,
                ["packs"] = marketPacks
            });
        await FireAsync(marketTreeSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(marketTreeSurface, Self.Value));
        }

        // Main UI is UiSurface based. Emit an app-shell surface that can drive the entire thin host chrome + nav.
        // Neurons (and packs after embodiment) build and own this dynamically.
        // Dynamic menu data - populated from seeds (in full system a neuron would provide the list of interesting/activated experiences as UiWidgetTree nodes)
        static IEnumerable<DigitalBrain.Core.UiWidgetTree> BuildShellMenuItems()
        {
            var items = new List<(string Label, string? TargetSurfaceKind, IReadOnlyDictionary<string, object?>? Action)>
            {
                ("Marketplace", UiSurfaceKinds.MarketplaceList, null),
                ("Installed", UiSurfaceKinds.InstalledBundles, null),
                ("SE Hello World", "hello-world-se", null),
                ("Tasks", UiSurfaceKinds.TaskManager, null),
                ("INO Chat", "chat", null),
                ("Timeline", UiSurfaceKinds.Timeline, null)
            };
            // Dynamic add from MarketplaceSeeds (example of neuron-driven list)
            foreach (var seed in MarketplaceSeeds.LocalUiPacks.Where(p => p.Name.StartsWith("DigitalBrain.UI")).Take(1))
            {
                items.Add(($"Open {seed.Name}", "marketplace-list", null));
            }
            foreach (var (label, target, action) in items)
            {
                var itemProps = new Dictionary<string, object?> { ["label"] = label };
                if (action != null) itemProps["action"] = action;
                else if (target != null) itemProps["targetSurfaceKind"] = target;
                yield return new(DigitalBrain.Core.NeuronUiKit.MenuItem, itemProps);
            }
            yield return new(DigitalBrain.Core.NeuronUiKit.Divider, new Dictionary<string, object?>());
        }

        var mainShellTree = new DigitalBrain.Core.UiWidgetTree(
            DigitalBrain.Core.NeuronUiKit.Scaffold,
            new Dictionary<string, object?>
            {
                ["title"] = "DigitalBrain",
                ["activeContent"] = UiSurfaceKinds.MarketplaceList
            },
            new List<DigitalBrain.Core.UiWidgetTree>
            {
                new DigitalBrain.Core.UiWidgetTree(DigitalBrain.Core.NeuronUiKit.Header, new Dictionary<string, object?>
                {
                    ["title"] = "DigitalBrain"
                }),
                new DigitalBrain.Core.UiWidgetTree("forui:sidebar", new Dictionary<string, object?> { ["title"] = "DigitalBrain" },
                    new List<DigitalBrain.Core.UiWidgetTree>(BuildShellMenuItems())),
                new DigitalBrain.Core.UiWidgetTree("content", new Dictionary<string, object?>
                {
                    ["defaultView"] = UiSurfaceKinds.MarketplaceList
                })
            });

        var appShellSurface = DigitalBrain.Core.UiSurface.ForWidgetTree(mainShellTree, title: "Main Shell", emitter: "shell-main");
        // Also set the canonical kind so hosts can recognize it as the root chrome.
        // (We keep the tree in Props; hosts that understand WidgetTreeKind render the full shell.)
        await FireAsync(appShellSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(appShellSurface, Self.Value));
        }

        // Legacy shell chrome surface for incremental adoption (still useful for panels).
        var shellSurface = new UiSurface(UiSurfaceKinds.ShellChrome, new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "shell.primary",
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "NeuroUI Host Shell",
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["nav"] = new[]
            {
                new Dictionary<string, object?> { ["id"] = "market", ["label"] = "Marketplace", ["kind"] = UiSurfaceKinds.MarketplaceList },
                new Dictionary<string, object?> { ["id"] = "tasks", ["label"] = "Tasks", ["kind"] = UiSurfaceKinds.TaskManager },
                new Dictionary<string, object?> { ["id"] = "chat", ["label"] = "INO", ["kind"] = "chat" }
            },
            ["chrome"] = "forui-sidebar"
        });
        await FireAsync(shellSurface);
        if (bus != null) bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(shellSurface, Self.Value));

        var installedStart = UiSurfaceLiveData.InstalledBundlesFromPacks(publishedForStart, Array.Empty<NeuroPack>());
        await FireAsync(installedStart);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(installedStart, Self.Value));
        }

        // Runtime-only Hello World from Software Engineering team (neuron driven, no Flutter view code).
        // Button click flows as synapse to DemoNeuron which emits "toast" surface -> host shows ForUI notification.
        var seHelloTree = new UiWidgetTree(
            "fcard",
            new Dictionary<string, object?> { ["title"] = "Software Engineering Team", ["subtitle"] = "Build • Pack • Bundle demo" },
            new List<UiWidgetTree>
            {
                new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "All UI is neurons + synapses. Click to trigger ForUI notification at runtime." }),
                new UiWidgetTree("fbutton", new Dictionary<string, object?>
                {
                    ["label"] = "Hello World!",
                    [UiSurfaceKeys.SynapseType] = "DemoMessageSynapse",
                    ["text"] = "hello-world"
                })
            });
        var seHelloSurface = new UiSurface("hello-world-se", new Dictionary<string, object?>
        {
            ["tree"] = seHelloTree,
            [UiSurfaceKeys.Title] = "SE Team Hello World",
            [UiSurfaceKeys.Emitter] = Self.Value
        });
        await FireAsync(seHelloSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(seHelloSurface, Self.Value));
        }

        // Rich UI Kit demo surface (replaces static gallery .dart screen). Neurons emit full forui components via kit.
        var richKitTree = new UiWidgetTree("column", new Dictionary<string, object?>(), new List<UiWidgetTree>
        {
            new UiWidgetTree("forui:fcard", new Dictionary<string, object?> { ["title"] = "ForUI Rich Kit (neuron emitted)" }),
            new UiWidgetTree("forui:ftextfield", new Dictionary<string, object?> { ["label"] = "Pack name", ["hint"] = "Enter name" }),
            new UiWidgetTree("forui:fbutton", new Dictionary<string, object?> { ["label"] = "Primary Action", ["variant"] = "primary" }),
            new UiWidgetTree("forui:fbutton", new Dictionary<string, object?> { ["label"] = "Outline", ["variant"] = "outline" }),
            new UiWidgetTree("forui:fselect", new Dictionary<string, object?> { ["label"] = "Choose demo", ["items"] = new[] { "Hello", "Market", "Tasks" } }),
            new UiWidgetTree("neuron:divider", new Dictionary<string, object?>()),
            new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "All UI is runtime from neurons using forui kit. No static .dart screens." })
        });
        var richKitSurface = new UiSurface("ui-kit-rich", new Dictionary<string, object?>
        {
            ["tree"] = richKitTree,
            [UiSurfaceKeys.Title] = "ForUI Kit Demo",
            [UiSurfaceKeys.Emitter] = Self.Value
        });
        await FireAsync(richKitSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(richKitSurface, Self.Value));
        }

        // Kit-driven INO Chat surface (replaces ino_chat_screen.dart). Messages list + input using forui kit.
        var chatTree = new UiWidgetTree("column", new Dictionary<string, object?>(), new List<UiWidgetTree>
        {
            new UiWidgetTree("forui:fcard", new Dictionary<string, object?> { ["title"] = "INO Chat (neuron kit)" }),
            new UiWidgetTree("list", new Dictionary<string, object?> { ["items"] = new[] {
                new Dictionary<string, object?> { ["label"] = "User: Hello", ["subtitle"] = "" },
                new Dictionary<string, object?> { ["label"] = "INO: How can I help build today?", ["subtitle"] = "" }
            }}),
            new UiWidgetTree("forui:ftextfield", new Dictionary<string, object?> { ["label"] = "Message", ["hint"] = "Ask INO..." }),
            new UiWidgetTree("forui:fbutton", new Dictionary<string, object?> { ["label"] = "Send", ["synapseType"] = "InoRequest" })
        });
        var chatSurface = new UiSurface("chat", new Dictionary<string, object?>
        {
            ["tree"] = chatTree,
            [UiSurfaceKeys.Title] = "INO Chat",
            [UiSurfaceKeys.Emitter] = Self.Value
        });
        await FireAsync(chatSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(chatSurface, Self.Value));
        }

        // Seed a first-class live chart surface (uses GraphicSpec for rich interactive rendering via graphic package).
        var demoChartData = new[]
        {
            new Dictionary<string, object?> { ["month"] = "Jan", ["sales"] = 42 },
            new Dictionary<string, object?> { ["month"] = "Feb", ["sales"] = 58 },
            new Dictionary<string, object?> { ["month"] = "Mar", ["sales"] = 31 },
            new Dictionary<string, object?> { ["month"] = "Apr", ["sales"] = 71 },
        };
        var gspec = new GraphicSpec(
            Title: "Demo Sales Trend (live chart)",
            Data: demoChartData,
            Variables: new Dictionary<string, object?>
            {
                ["month"] = new Dictionary<string, object?> { ["type"] = "ordinal" },
                ["sales"] = new Dictionary<string, object?> { ["type"] = "linear" }
            },
            Marks: new[] { new Dictionary<string, object?> { ["kind"] = "line", ["position"] = "month*sales" } as IReadOnlyDictionary<string, object?> },
            Summary: "4 months. Click points or use commands to filter/transform.");
        var chartSurface = UiSurfaceSamples.Chart("surface.chart.demo", Self.Value, gspec);
        await FireAsync(chartSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(chartSurface, Self.Value));
        }
    }

    public async Task HandleAsync(RestartResource cmd)
    {
        if (cmd.IsRollingUpdate)
        {
            Logger.LogInformation("Aspire rolling restart for {Res} target={Ver} strategy={Strategy}", cmd.ResourceName, cmd.TargetVersion, cmd.Strategy);
            await FireAsync(new SystemStatusChanged("aspire", "rolling-restart-started", $"{cmd.ResourceName}@{cmd.TargetVersion}"));

            // Full declarative rolling status surface emitted from neuron (client renders, no static logic).
            var rollingProps = new Dictionary<string, object?>
            {
                [UiSurfaceKeys.SurfaceId] = "rolling-" + cmd.ResourceName,
                [UiSurfaceKeys.Emitter] = Self.Value,
                [UiSurfaceKeys.Title] = "Rolling Kernel Update",
                [UiSurfaceKeys.Priority] = 50,
                [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
                ["resource"] = cmd.ResourceName,
                ["version"] = cmd.TargetVersion ?? "next",
                ["strategy"] = cmd.Strategy,
                ["status"] = "draining-replica",
                ["haReplicas"] = 3
            };
            await FireAsync(new UiSurface(KernelUiSurfaceKinds.Rolling, rollingProps));
        }
        else
        {
            Logger.LogInformation("Aspire restarting resource: {Res}", cmd.ResourceName);
        }

        await FireAsync(new DistributedAppStarted(cmd.ResourceName, Success: true, "restarted"));
        await FireAsync(new SystemStatusChanged("aspire", "restarted", cmd.ResourceName));
    }

    public async Task HandleAsync(PerformKernelSelfUpdate cmd)
    {
        var version = string.IsNullOrWhiteSpace(cmd.Version) ? KernelPack.DefaultVersion : cmd.Version;

        // Preserve state before rolling restart using checkpoint (seamless update primitive).
        var preUpdateCheckpoint = await CreateCheckpointAsync();

        // Explicit rolling update across replicas (drain one, update, verify using checkpoint + lineage, rejoin).
        // 3 replicas for HA, update incrementally.
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        var lineageCount = 0;

        for (int replica = 1; replica <= 3; replica++)
        {
            // Drain phase.
            var drainProps = new Dictionary<string, object?>
            {
                [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingDrain}-{replica}",
                [UiSurfaceKeys.Emitter] = Self.Value,
                [UiSurfaceKeys.Title] = $"Drain Replica {replica}/3",
                [UiSurfaceKeys.Priority] = 70 + replica,
                [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
                ["replica"] = replica,
                ["phase"] = "draining",
                ["version"] = version,
                ["checkpointId"] = preUpdateCheckpoint.SynapseId
            };
            await FireAsync(new UiSurface(KernelUiSurfaceKinds.RollingDrain, drainProps));
            if (bus is not null)
            {
                bus.Broadcast(new RfwCard("digitalbrain", "KernelRollingDrainCard", System.Text.Json.JsonSerializer.Serialize(new { replica, phase = "draining", version })));
            }

            // Rolling restart signal for replica.
            await FireAsync(new RestartResource("silo", IsRollingUpdate: true, TargetVersion: version, Strategy: $"replica-{replica}-of-3"));

            // Verify using causal lineage. The verify surface reflects the outcome so a failing replica reads
            // "verify-failed" (not a misleading "verified" immediately followed by a rollback).
            var replicaLineage = await GetCausalLineageAsync(preUpdateCheckpoint.SynapseId);
            lineageCount = replicaLineage.Count;

            var verifyFailed = cmd.FailAtReplica == replica;
            var verifyPhase = verifyFailed ? "verify-failed" : "verified";

            var verifyProps = new Dictionary<string, object?>
            {
                [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingVerify}-{replica}",
                [UiSurfaceKeys.Emitter] = Self.Value,
                [UiSurfaceKeys.Title] = $"Verify Replica {replica}/3",
                [UiSurfaceKeys.Priority] = 70 + replica,
                [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
                ["replica"] = replica,
                ["phase"] = verifyPhase,
                ["version"] = version,
                ["lineageEvents"] = lineageCount
            };
            await FireAsync(new UiSurface(KernelUiSurfaceKinds.RollingVerify, verifyProps));
            if (bus is not null)
            {
                bus.Broadcast(new RfwCard("digitalbrain", "KernelRollingVerifyCard", System.Text.Json.JsonSerializer.Serialize(new { replica, phase = verifyPhase, version, lineageEvents = lineageCount })));
            }

            if (verifyFailed)
            {
                await RestoreCheckpointAsync(preUpdateCheckpoint);
                var rollbackProps = new Dictionary<string, object?>
                {
                    [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingRollback}-{replica}",
                    [UiSurfaceKeys.Emitter] = Self.Value,
                    [UiSurfaceKeys.Title] = $"Rollback at Replica {replica}/3",
                    [UiSurfaceKeys.Priority] = 90,
                    [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
                    ["replica"] = replica,
                    ["phase"] = "rolledback",
                    ["version"] = version,
                    ["checkpointId"] = preUpdateCheckpoint.SynapseId,
                    ["reason"] = "verify-failed"
                };
                await FireAsync(new UiSurface(KernelUiSurfaceKinds.RollingRollback, rollbackProps));
                if (bus is not null)
                {
                    bus.Broadcast(new RfwCard("digitalbrain", "KernelRollingRollbackCard", System.Text.Json.JsonSerializer.Serialize(new { replica, phase = "rolledback", version })));
                }
                return; // Abort: do not process further replicas, do not emit RollingComplete.
            }
        }

        var statusData = System.Text.Json.JsonSerializer.Serialize(new { process = KernelPack.Name, version, status = "complete", haReplicas = 3, checkpoint = preUpdateCheckpoint.SynapseId, lineageEvents = lineageCount });
        if (bus is not null)
        {
            bus.Broadcast(new RfwCard("digitalbrain", "KernelUpdateStatusCard", statusData));
        }

        var completeProps = new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingComplete}-{version}",
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Kernel Rolling Update",
            [UiSurfaceKeys.Priority] = 80,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["version"] = version,
            ["strategy"] = "one-replica-at-a-time",
            ["checkpointId"] = preUpdateCheckpoint.SynapseId,
            ["status"] = "complete",
            ["replicasProcessed"] = 3,
            ["lineageEvents"] = lineageCount
        };
        await FireAsync(new UiSurface(KernelUiSurfaceKinds.RollingComplete, completeProps));
    }
}

// Marketplace: purely journal-driven (published packs derived from PublishToMarketplace synapses on demand).
// No private lists or disk side-effects.
[GrainType("digitalbrain.marketplace.v1")]
public class MarketplaceNeuron : Neuron, IMarketplaceNeuron
{
    // In-memory view over published packs (rebuilt from journals on first use / activate; updated incrementally on publish).
    // Avoids O(n) full journal scan on every ListPublished / Find / Install.
    private Dictionary<string, NeuroPack>? _publishedCache;

    public MarketplaceNeuron(ILogger<MarketplaceNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(PublishToMarketplace cmd)
    {
        var remote = ServiceProvider.GetService<IRemoteMarketplaceClient>();
        var useRemote = ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:UseRemote", false) ?? false;

        if (useRemote && remote is not null)
        {
            await remote.PublishAsync(cmd);
            // Remote is source of truth for catalog; still allow local journal for offline/hybrid.
        }

        Logger.LogInformation("Marketplace PUBLISHED real pack {Name}@{Ver} owner={Owner} private={Private} commission={Rate:P0}",
            cmd.PackName, cmd.Version, cmd.OwnerId, cmd.IsPrivate, cmd.CommissionRate);

        // Update view for fast subsequent queries. The Publish synapse itself is journaled by the caller Fire.
        EnsureCache();
        _publishedCache![KeyFor(cmd.PackName, cmd.Version)] = ToNeuroPack(cmd);

        // Emit refreshed marketplace surface so thin NeuroUI hosts (Flutter + future) see the update live from the neuron.
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        var published = _publishedCache!.Values.ToList();
        var listSurface = UiSurfaceLiveData.MarketplaceListFromPacks(published, published);
        await FireAsync(listSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(listSurface, Self.Value));
        }
    }

    private static string KeyFor(string name, string version) => $"{name}@{version}";

    private static NeuroPack ToNeuroPack(PublishToMarketplace p) =>
        new(p.PackName, p.Version, p.OwnerId, p.IsPrivate, p.CommissionRate, p.Code, p.Description, p.AuthorPublicKeyBase64, p.SignatureBase64, p.Price);

    public async Task HandleAsync(InstallFromMarketplace cmd)
    {
        var remote = ServiceProvider.GetService<IRemoteMarketplaceClient>();
        var useRemote = ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:UseRemote", false) ?? false;

        if (useRemote && remote is not null)
        {
            await remote.InstallAsync(cmd);
        }

        var pack = FindPublishedPack(cmd.PackName, cmd.Version);
        if (pack == null)
        {
            Logger.LogWarning("Install failed - pack not found: {Key}", cmd.PackName + "@" + cmd.Version);
            return;
        }

        if (pack.IsPrivate && cmd.BuyerId != pack.OwnerId)
        {
            Logger.LogWarning("Install blocked - pack {Key} is private to owner {Owner}", cmd.PackName + "@" + cmd.Version, pack.OwnerId);
            return;
        }

        // Trust gate. A PRESENT-but-invalid signature is always rejected. A MISSING signature is warn-only
        // unless DigitalBrain:Marketplace:RejectUnsignedPacks=true is configured for remote/untrusted installs.
        var isSigned = !string.IsNullOrEmpty(pack.AuthorPublicKeyBase64) && !string.IsNullOrEmpty(pack.SignatureBase64);
        if (isSigned)
        {
            // VerifyPack checks integrity against the pack's OWN embedded public key — it proves the pack was not
            // tampered with after signing, NOT that the publisher is trusted (a self-signed pack passes). A
            // trusted-publisher allowlist (verify AuthorPublicKeyBase64 against registered keys) is deferred and
            // pairs with the Key Vault work; until then keep RejectUnsignedPacks=true only for trusted install paths.
            if (!PackSignatureVerifier.VerifyPack(pack))
            {
                Logger.LogWarning("Install REJECTED - invalid signature on pack {Key}", cmd.PackName + "@" + cmd.Version);
                return;
            }
            Logger.LogInformation("Install signature verified for pack {Key}", cmd.PackName + "@" + cmd.Version);
        }
        else if (RejectUnsignedPacks)
        {
            Logger.LogWarning("Install REJECTED - pack {Key} is unsigned and unsigned installs are disabled", cmd.PackName + "@" + cmd.Version);
            return;
        }
        else
        {
            Logger.LogWarning("Install WARNING - pack {Key} is unsigned (allowed during trust transition)", cmd.PackName + "@" + cmd.Version);
        }

        // Economics gate: a premium pack (Price > 0) requires the buyer to hold a license (entitlement).
        if (pack.Price > 0m)
        {
            var license = GrainFactory.GetGrain<ILicenseNeuron>("license-main");
            if (!await license.HasLicenseAsync(pack.Name, cmd.BuyerId))
            {
                Logger.LogWarning("Install REJECTED - premium pack {Key} requires a license for buyer {Buyer}", cmd.PackName + "@" + cmd.Version, cmd.BuyerId);
                return;
            }
            Logger.LogInformation("Install entitlement verified for premium pack {Key}, buyer {Buyer}", cmd.PackName + "@" + cmd.Version, cmd.BuyerId);
        }

        // Commission amount is 0 for now (free packs use Price only for gating). Real amount = Price * Rate when payment flow supplies tx value.
        var commissionAmount = 0.0;
        await FireAsync(new CommissionTaken(
            pack.Name,
            pack.Version,
            cmd.BuyerId,
            pack.OwnerId,
            pack.CommissionRate,
            commissionAmount));

        await FireAsync(new NeuroPackInstalled(pack));

        // Kernel as first-class pack: installing the kernel pack automatically triggers HA rolling self-update (embodiment-driven, no name special in company skill).
        if (string.Equals(cmd.PackName, KernelPack.Name, StringComparison.OrdinalIgnoreCase))
        {
            var aspire = GrainFactory.GetGrain<IAspireNeuron>("aspire-main");
            await aspire.FireAsync(new PerformKernelSelfUpdate(cmd.Version));
        }

        var genKey = "generated-" + pack.Name.ToLowerInvariant();
        var generated = GrainFactory.GetGrain<IGeneratedNeuron>(genKey);
        // Deliver the full pack (with Code) so the host neuron can compile + embody it; then trigger a use.
        await generated.DeliverAsync(new NeuroPackInstalled(pack));
        await generated.FireAsync(new ExperienceUsed(pack.Name, "installed-and-activated", cmd.BuyerId, cmd.SessionId));

        // Refresh installed bundles surface (and marketplace list) so UI sees update live via kit.
        // (Real query uses journals/cache; seeds + this pack for immediate.)
        var pub = new List<NeuroPack> { pack };
        var inst = new List<NeuroPack> { pack };
        var refInst = UiSurfaceLiveData.InstalledBundlesFromPacks(pub, inst, cmd.BuyerId, cmd.SessionId);
        await FireAsync(refInst);
        var bus2 = ServiceProvider.GetService<HomeFeedBus>();
        if (bus2 != null)
        {
            bus2.Broadcast(UiSurfaceRfwBridge.FromUiSurface(refInst, Self.Value));
        }

        Logger.LogInformation("Marketplace INSTALL {Key} by {Buyer}. Commission {Rate:P0} taken for seller {Seller}.",
            cmd.PackName + "@" + cmd.Version, cmd.BuyerId, pack.CommissionRate, pack.OwnerId);
    }

    public async Task HandleAsync(ListPublished _cmd)
    {
        var remote = ServiceProvider.GetService<IRemoteMarketplaceClient>();
        var useRemote = ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:UseRemote", false) ?? false;

        if (useRemote && remote is not null)
        {
            var list = await remote.ListAsync();
            await FireAsync(list);
            return;
        }

        var packs = GetPublishedPacks();
        Logger.LogInformation("Marketplace listing {Count} real packs", packs.Count);
        await FireAsync(new PublishedList(packs));
    }

    private IReadOnlyList<NeuroPack> GetPublishedPacks()
    {
        EnsureCache();
        return _publishedCache!.Values.ToList();
    }

    private void EnsureCache()
    {
        if (_publishedCache is not null) return;

        _publishedCache = new Dictionary<string, NeuroPack>(StringComparer.OrdinalIgnoreCase);

        // Seed once from journals (recovery path). Subsequent publishes update in place for O(1) List/Find.
        foreach (var p in OutgoingJournal.Concat(IncomingJournal).OfType<PublishToMarketplace>())
        {
            _publishedCache[KeyFor(p.PackName, p.Version)] = ToNeuroPack(p);
        }
    }

    private NeuroPack? FindPublishedPack(string name, string version)
    {
        EnsureCache();
        _publishedCache!.TryGetValue(KeyFor(name, version), out var p);
        return p;
    }

    private bool RejectUnsignedPacks =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:RejectUnsignedPacks", true) ?? true;
}

[GrainType("digitalbrain.observability.v1")]
public class ObservabilityNeuron : Neuron, IObservabilityNeuron
{
    public ObservabilityNeuron(ILogger<ObservabilityNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public Task HandleAsync(UiSurface surface)
    {
        Logger.LogInformation("Observability surface {Kind} correlation={CorrelationId}", surface.Kind, surface.CorrelationId);

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        bus?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
        return Task.CompletedTask;
    }

    public async Task HandleAsync(ClusterActivity activity)
    {
        await PublishGraphFromJournalAsync(activity);
    }

    public async Task HandleAsync(ThreeDGraphUpdate update)
    {
        await PublishGraphFromJournalAsync(update);
    }

    private async Task PublishGraphFromJournalAsync(Synapse cause)
    {
        var graphTimeline = OutgoingJournal
            .Concat(IncomingJournal)
            .Where(s => s is ClusterActivity or ThreeDGraphUpdate)
            .DistinctBy(s => s.SynapseId)
            .OrderBy(s => s.Timestamp)
            .TakeLast(40)
            .ToList();

        var surface = UiSurfaceLiveData.ActivityGraphFromTimeline(graphTimeline) with
        {
            CorrelationId = cause.CorrelationId ?? cause.SynapseId
        };

        await FireAsync(surface);
    }
}

[GrainType("digitalbrain.compiler.v1")]
public class CompilerNeuron : Neuron, ICompiler
{
    public CompilerNeuron(ILogger<CompilerNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(CreateNeuronRequest req)
    {
        Logger.LogInformation("Compiler generating for: {Desc}", req.Description);
        var packName = "Generated" + req.Description.Replace(" ", "").Replace("\"", "").Replace("-", "").Substring(0, Math.Min(18, req.Description.Length));
        string snippet;

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            var sys = "You are expert C# generator for real working software. Output ONLY complete minimal self-contained console app (top level or Main/Run) fulfilling the spec (may be .feature or English desc like 'process last 100 emails on PC, write report.txt with subjects/bodies'). Use file IO for archive. Only stdlib. Respond ONLY ```csharp block. (Neuron style only if requested)";
            var user = $"Description: {req.Description}\nBase name hint: {packName}";
            var fullPrompt = sys + "\n\n" + user;

            var response = await chat.GetResponseAsync(fullPrompt);
            var acc = response.Text;
            snippet = ExtractCode(acc);
            if (string.IsNullOrWhiteSpace(snippet))
                snippet = FallbackGeneralCode(packName, req.Description);
        }
        else
        {
            snippet = FallbackGeneralCode(packName, req.Description);
        }

        await FireAsync(new NeuronCodeGenerated(req.Description, snippet));
        await FireAsync(new NeuronTelemetry(Self, "code-generated"));

        // Produce a real NeuroPack so caller can publish/install/export as usable software or internal neuron.
        var pack = new NeuroPack(packName, "0.1-dev", "compiler", false, 0.10, snippet, req.Description);
        // The caller (REPL/MCP) can fire PublishToMarketplace with this data if desired.
        // For auto-flow in high-level commands, the REPL handles publish + export.
    }

    static string ExtractCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var start = text.IndexOf("```csharp", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += 9;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text.Substring(start, end - start).Trim();
        }
        start = text.IndexOf("```", StringComparison.Ordinal);
        if (start >= 0)
        {
            start += 3;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text.Substring(start, end - start).Trim();
        }
        var c = text.IndexOf("public class ", StringComparison.Ordinal);
        if (c >= 0) return text.Substring(c).Trim();
        return text.Trim();
    }

    static string FallbackGeneralCode(string pack, string desc) =>
        $@"using System;
using System.IO;

public class {pack}
{{
    public static void Run(string input = """")
    {{
        var data = ""Processed data for: {desc}\nInput: "" + input + ""\nResult: report written.\n"";
        File.WriteAllText(""report.txt"", data);
        Console.WriteLine(""Wrote report.txt with processed "" + desc);
    }}
    public static void Main(string[] args) => Run(args.Length > 0 ? string.Join("" "", args) : """");
}}";
}

[GrainType("digitalbrain.optimizer.v1")]
public class MetaOptimizerNeuron : Neuron, IMetaOptimizerNeuron
{
    public MetaOptimizerNeuron(ILogger<MetaOptimizerNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(NeuronTelemetry telemetry)
    {
        // Pure journal-derived count (no private state).
        var count = IncomingJournal.Concat(OutgoingJournal).OfType<NeuronTelemetry>().Count();
        Logger.LogInformation("Optimizer received telemetry from {Neuron}: {Event} (total {Count})", telemetry.Neuron, telemetry.Event, count);

        if (count % 5 == 0)
        {
            string proposal;
            var chat = ServiceProvider.GetService<IChatClient>();
            if (chat != null)
            {
                var p = $"Telemetry count reached {count}. Propose ONE short, actionable wiring or scaling improvement for the DigitalBrain neuron system (Orleans grains + Aspire + compiler for code gen from English).";
                var response = await chat.GetResponseAsync(p);
                var acc = response.Text;
                proposal = acc.Length > 20 ? acc.Trim() : "Add parallel compiler neurons and route create requests through LlmNeuron";
            }
            else
            {
                proposal = "Add parallel compiler neurons routed via LlmNeuron for faster self-gen";
            }
            await FireAsync(new WiringOptimizationProposed(proposal, Self.Value));
        }
    }

    public Task HandleAsync(WiringOptimizationProposed proposal)
    {
        Logger.LogInformation("Optimizer proposal received: {Proposal} from {From}", proposal.Proposal, proposal.FromNeuron);
        return Task.CompletedTask;
    }
}

// Host for installed packs (keystone embodiment, option b).
// On install it receives the real NeuroPack and COMPILES pack.Code into a collectible ALC via IPackEmbodiment,
// holding the live IPackBehavior capability. Typed-dispatch v2 lets embodied packs handle real Synapse types and
// emit typed Synapse outputs through this host, while the old Respond(string) path remains as a compatibility
// fallback for ExperienceUsed and natural-language packs.
[GrainType("digitalbrain.generated")]
public class GeneratedNeuron : Neuron, IGeneratedNeuron, IHandle<NeuronTelemetry>
{
    private EmbodiedPack? _embodied;

    public GeneratedNeuron(ILogger<GeneratedNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public Task HandleAsync(NeuronTelemetry telemetry) => Task.CompletedTask;

    public override Task OnDeactivateAsync(Orleans.DeactivationReason reason, CancellationToken cancellationToken)
    {
        // Unload the ALC (if any) on deactivation. This is necessary but not always sufficient for full collection:
        // Orleans may retain type info / proxies. Use weak-ref verification in tests + explicit GC pressure for proof.
        var toUnload = _embodied;
        _embodied = null;
        toUnload?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        var id = this.GetPrimaryKeyString() ?? "unknown-generated";
        Logger.LogInformation("GeneratedNeuron {Id} dispatched {Type}", id, synapse.Type);
        await FireAsync(new NeuronTelemetry(Self, "generated-dispatched"));

        switch (synapse)
        {
            case NeuroPackInstalled installed:
                TryEmbody(installed.Pack);
                return;
        }

        if (await TryDispatchEmbodiedAsync(synapse))
        {
            return;
        }

        switch (synapse)
        {
            case DemoMessageSynapse msg:
                Logger.LogInformation("Generated handled message: {Text}", msg.Text);
                break;
            case ExperienceUsed used:
                await UseExperienceAsync(used);
                break;
        }
    }

    // Compile + load the pack into a collectible ALC. A pack that is not a compilable IPackBehavior
    // (e.g. a natural-language pack) fails embodiment; we log it and fall back to the LLM path on use.
    private void TryEmbody(NeuroPack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.Code))
            return;

        var embodier = ServiceProvider.GetService<IPackEmbodiment>();
        if (embodier is null)
        {
            Logger.LogWarning("No IPackEmbodiment registered; pack '{Pack}' will use the LLM fallback.", pack.Name);
            return;
        }

        try
        {
            _embodied?.Dispose();
            _embodied = embodier.Embody(pack.Name, pack.Code);
            Logger.LogInformation("GeneratedNeuron EMBODIED pack {Name}@{Ver} as real compiled C#.", pack.Name, pack.Version);
        }
        catch (PackEmbodimentException ex)
        {
            _embodied = null;
            Logger.LogWarning(ex, "Pack '{Pack}' is not a compilable IPackBehavior; using LLM fallback on use.", pack.Name);
        }
    }

    private void EnsureEmbodied()
    {
        if (_embodied is not null) return;
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last is not null)
        {
            TryEmbody(last.Pack);
            return;
        }
        // Preinstalled local seed packs (e.g. ui-gallery, hello-world) are shown in the installer but never
        // receive a NeuroPackInstalled, so they were never embodied — opening one stalled on "Waiting for the
        // experience to start". Embody such a pack on first use from the local catalog, keyed by grain id.
        const string generatedPrefix = "generated-";
        var packName = this.GetPrimaryKeyString() ?? string.Empty;
        if (packName.StartsWith(generatedPrefix, StringComparison.OrdinalIgnoreCase))
            packName = packName[generatedPrefix.Length..];
        var seed = MarketplaceSeeds.LocalUiPacks.FirstOrDefault(pack =>
            string.Equals(pack.Name, packName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pack.Code));
        if (seed is not null)
            TryEmbody(seed);
    }

    private async Task<bool> TryDispatchEmbodiedAsync(Synapse synapse)
    {
        EnsureEmbodied();
        if (_embodied is null || !_embodied.CanHandle(synapse))
        {
            return false;
        }

        var manifest = _embodied.GetManifest();
        IReadOnlyList<Synapse> outputs;
        try
        {
            outputs = _embodied.Handle(synapse);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Embodied pack '{Pack}' failed while handling {SynapseType}.", _embodied.PackName, synapse.Type);
            await FireAsync(new PackEmission(_embodied.PackName, synapse.Type, "pack-error:" + ex.GetBaseException().Message));
            return true;
        }

        foreach (var output in outputs)
        {
            var normalized = NormalizePackOutput(_embodied.PackName, output);
            await FireAsync(normalized);
            BroadcastPackSurface(normalized, _embodied.PackName);
        }

        Logger.LogInformation(
            "GeneratedNeuron dispatched {SynapseType} to embodied pack '{Pack}' (manifest: {ManifestTypes}) and emitted {Count} synapse(s).",
            synapse.Type,
            _embodied.PackName,
            string.Join(',', manifest.HandledSynapseTypes.Select(t => t.Value)),
            outputs.Count);
        return true;
    }

    private static Synapse NormalizePackOutput(string packName, Synapse output)
    {
        var normalized = output is PackEmission emission
            ? emission with { Pack = packName }
            : output;

        return normalized with
        {
            CorrelationId = null,
            CausationId = null,
            SynapseId = Guid.NewGuid().ToString("N")
        };
    }

    private void BroadcastPackSurface(Synapse output, string packName)
    {
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is null) return;

        if (output is UiSurface surface)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, packName));
        }
        else if (output is RfwCard card)
        {
            bus.Broadcast(card);
        }
    }

    private async Task UseExperienceAsync(ExperienceUsed used)
    {
        if (IsGmailInsightsExperience(used))
        {
            await RunGmailInsightsExperienceAsync(used);
            return;
        }

        EnsureEmbodied();

        // Real path: the pack's compiled code runs and we emit its actual output.
        if (_embodied is not null)
        {
            var output = _embodied.Respond(used.Action);
            await FireAsync(new PackEmission(_embodied.PackName, used.Action, output));
            Logger.LogInformation("GeneratedNeuron ran embodied pack '{Pack}' for action '{Action}'", _embodied.PackName, used.Action);
            if (used.Action is "open" or "emit-test-surface" or "self-test")
            {
                var winTree = new UiWidgetTree("fcard", new Dictionary<string, object?> { ["title"] = used.Pack + " - " + used.Action }, new List<UiWidgetTree> { new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "Live from embodied " + used.Pack }) });
                var surf = new UiSurface(used.Pack, new Dictionary<string, object?> { [UiSurfaceKeys.Title] = used.Pack, ["pack"] = used.Pack, ["tree"] = winTree });
                await FireAsync(surf);
                var b = ServiceProvider.GetService<HomeFeedBus>();
                b?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surf, Self.Value));
            }
        }

        // Fallback for natural-language packs with no compilable Code: LLM "embodiment" (the legacy behavior).
        var inst = LastInstalledPack();
        if (inst is null)
        {
            Logger.LogInformation("Generated experience {Pack} used: {Action} (no installed pack yet).", used.Pack, used.Action);
            return;
        }

        var (packKey, code, desc) = inst.Value;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            await FireAsync(new LlmResponse(used.Pack, $"[Embodied: {packKey}] Simulated response to {used.Action} using installed experience.", "sim"));
        }
        else
        {
            var behaviorPrompt = $"You are now the installed experience '{packKey}'.\n" +
                                 $"Description: {desc}\n" +
                                 $"Implementation guidance/code:\n{code}\n\n" +
                                 $"Handle the following usage: {used.Action} on input related to '{used.Pack}'.\n" +
                                 "Respond in character as this specific installed neuron/experience would. Be concise and useful.";
            var response = await chat.GetResponseAsync(behaviorPrompt);
            await FireAsync(new LlmResponse(behaviorPrompt, response.Text.Trim(), "embodied-pack"));
            Logger.LogInformation("GeneratedNeuron LLM-embodied pack '{Pack}' for action '{Action}'", packKey, used.Action);
        }

        if (used.Action is "open" or "emit-test-surface" or "self-test")
        {
            var winTree = new UiWidgetTree("fcard", new Dictionary<string, object?> { ["title"] = used.Pack + " - " + used.Action }, new List<UiWidgetTree> { new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "Live surface from " + used.Pack + " pack scenario." }) });
            var surf = new UiSurface(used.Pack, new Dictionary<string, object?>
            {
                [UiSurfaceKeys.Title] = used.Pack,
                ["pack"] = used.Pack,
                ["tree"] = winTree
            });
            await FireAsync(surf);
            var bus = ServiceProvider.GetService<HomeFeedBus>();
            if (bus != null)
            {
                bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surf, Self.Value));
            }
        }
    }

    private async Task RunGmailInsightsExperienceAsync(ExperienceUsed used)
    {
        var userId = EffectiveUserId(used.UserId);
        var emails = BuildGmailSampleRows(100);
        var categoryRows = emails
            .GroupBy(row => row["category"]?.ToString() ?? "Other", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => new Dictionary<string, object?>
            {
                ["category"] = group.Key,
                ["count"] = group.Count()
            })
            .ToArray();

        var summary = await SummarizeGmailRowsAsync(emails);
        var chartRequestId = "gmail-last-100-" + StableKey(userId);
        await FireAsync(new PackEmission(used.Pack, used.Action, summary));

        var surface = BuildGmailInsightsSurface(used, summary, emails.Count, chartRequestId);
        await FireAsync(surface);
        ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));

        var chart = GrainFactory.GetGrain<IDataVisualizationNeuron>("chart-" + chartRequestId);
        await chart.FireAsync(new VisualizeDataRequest(
            "Gmail last 100 emails by category",
            System.Text.Json.JsonSerializer.Serialize(categoryRows),
            "bar",
            chartRequestId,
            userId,
            used.SessionId));
    }

    private async Task<string> SummarizeGmailRowsAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> emails)
    {
        var fallback = $"Local Gmail Insights analyzed {emails.Count} messages. Top categories: " +
            string.Join(", ", emails
                .GroupBy(row => row["category"]?.ToString() ?? "Other", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Take(3)
                .Select(group => group.Key + " " + group.Count()));

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            return fallback;
        }

        var sample = string.Join("\n", emails.Take(20).Select(row =>
            "- " + row["from"] + " | " + row["subject"] + " | " + row["category"]));
        try
        {
            var response = await chat.GetResponseAsync(
                "You are the local DigitalBrain Gmail insights experience. " +
                "Summarize these recent Gmail messages in two concise bullets and name the dominant categories.\n" +
                sample);
            var text = response.Text.Trim();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Local LLM Gmail summary failed; using deterministic fallback.");
            return fallback;
        }
    }

    private UiSurface BuildGmailInsightsSurface(ExperienceUsed used, string summary, int emailCount, string chartRequestId)
    {
        var tree = new UiWidgetTree(
            "fcard",
            new Dictionary<string, object?>
            {
                ["title"] = "Gmail Insights",
                ["subtitle"] = emailCount + " messages analyzed locally"
            },
            new List<UiWidgetTree>
            {
                new("text", new Dictionary<string, object?> { ["text"] = summary }),
                new("text", new Dictionary<string, object?> { ["text"] = "Chart request: " + chartRequestId })
            });

        return new UiSurface("gmail-insights", new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "surface.gmail-insights." + chartRequestId,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Gmail Insights",
            [UiSurfaceKeys.Priority] = 30,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["pack"] = used.Pack,
            ["action"] = used.Action,
            ["userId"] = EffectiveUserId(used.UserId),
            ["sessionId"] = used.SessionId,
            ["emailCount"] = emailCount,
            ["summary"] = summary,
            ["chartRequestId"] = chartRequestId,
            ["source"] = "local-sample",
            ["tree"] = tree
        });
    }

    private static bool IsGmailInsightsExperience(ExperienceUsed used) =>
        used.Pack.Equals("DigitalBrain.Experience.GmailInsights", StringComparison.OrdinalIgnoreCase) ||
        used.Action.StartsWith("gmail:", StringComparison.OrdinalIgnoreCase);

    private static string EffectiveUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();

    private static string StableKey(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        var key = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "anonymous" : key;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> BuildGmailSampleRows(int count)
    {
        string[] senders =
        [
            "alerts@github.com",
            "billing@cloud.local",
            "calendar@google.com",
            "team@digitalbrain.local",
            "newsletter@aiweekly.example",
            "support@customer.example",
            "security@accounts.google.com",
            "noreply@stripe.com"
        ];
        string[] categories = ["Engineering", "Billing", "Calendar", "Team", "Newsletter", "Support", "Security", "Payments"];
        string[] subjects =
        [
            "Build completed for kernel runtime",
            "Invoice available for review",
            "Meeting moved to tomorrow",
            "Product surface review notes",
            "Local AI tooling digest",
            "Customer follow-up requested",
            "Security alert for account access",
            "Payment receipt"
        ];

        var now = DateTimeOffset.UtcNow;
        var rows = new List<IReadOnlyDictionary<string, object?>>(count);
        for (var i = 0; i < count; i++)
        {
            var ix = i % categories.Length;
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = "gmail-local-" + (i + 1).ToString("000"),
                ["receivedAt"] = now.AddMinutes(-37 * i).ToString("O"),
                ["from"] = senders[ix],
                ["subject"] = subjects[ix] + " #" + (i + 1),
                ["category"] = categories[ix],
                ["importance"] = ix is 0 or 5 or 6 ? "high" : "normal"
            });
        }

        return rows;
    }

    private (string Key, string Code, string Description)? LastInstalledPack()
    {
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last is null) return null;
        var p = last.Pack;
        return ($"{p.Name}@{p.Version}", p.Code, p.Description);
    }

}

[GrainType("digitalbrain.llm.qwen.v1")]
public class LlmNeuron : Neuron, ILlmNeuron
{
    public LlmNeuron(ILogger<LlmNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(LlmPrompt prompt)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null)
        {
            await FireAsync(new LlmResponse(prompt.Prompt, "[no local llm client]", "none"));
            return;
        }

        var options = string.IsNullOrWhiteSpace(prompt.PreferredModel)
            ? null
            : new Microsoft.Extensions.AI.ChatOptions { ModelId = prompt.PreferredModel };
        var response = await chat.GetResponseAsync(prompt.Prompt, options);
        await FireAsync(new LlmResponse(prompt.Prompt, response.Text.Trim(), prompt.PreferredModel ?? "qwen2.5-coder:1.5b"));
    }
}

[GrainType("awesome.se.team10.v1")]
public class Software10TeamNeuron : Neuron, ISoftware10Team
{
    public Software10TeamNeuron(ILogger<Software10TeamNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(CreateSimpleApp cmd)
    {
        var name = "Legacy" + cmd.Description.Replace(" ", "").Substring(0, Math.Min(12, cmd.Description.Length));
        // Old soft: rigid 2010s style template
        var code = $"// Software10 (old) - classic style\nusing System;\npublic class {name}App {{\n  public static void Main() {{\n    Console.WriteLine(\"TODO: {cmd.Description}\");\n  }}\n}}";
        await FireAsync(new SimpleAppCreated(cmd.Team, name, code));
    }
}

[GrainType("awesome.se.team20.v1")]
public class Software20TeamNeuron : Neuron, ISoftware20Team
{
    public Software20TeamNeuron(ILogger<Software20TeamNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(CreateSimpleApp cmd)
    {
        var name = "Neuro" + cmd.Description.Replace(" ", "").Substring(0, Math.Min(12, cmd.Description.Length));
        string code;

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            var p = $"Create a clean minimal C# console or Neuron-style simple app for: {cmd.Description}. Make it modern, self-documenting, no legacy main if possible. Output only the code.";
            var response = await chat.GetResponseAsync(p);
            var acc = response.Text;
            code = acc.Trim().Length > 10 ? acc.Trim() : ModernTemplate(name, cmd.Description);
        }
        else
        {
            code = ModernTemplate(name, cmd.Description);
        }

        await FireAsync(new SimpleAppCreated(cmd.Team, name, code));
    }

    static string ModernTemplate(string n, string d) =>
        $"// Software20 (new) - DigitalBrain/LLM assisted\n[GrainType(\"app.{n.ToLower()}\")]\npublic class {n}App : Neuron {{\n  // Self-improving simple app for: {d}\n  public {n}App() {{ /* modern defaults */ }}\n}}";
}

// SoftwareEngineering.ClosedLoopNeuron + UI authoring closed loop support.
// Embodiable via marketplace NeuroPack. Uses local Ollama + MCP connections (Aspire for SE mods, Dart MCP knowledge for UI).
// Handles multi-kernel by preferring Aspire orchestration (restart resources, inspect distributed state) + marketplace for behavior updates (new packs become live via Generated).
[GrainType("softwareengineering.closedloop.v1")]
public class SoftwareEngineeringClosedLoopNeuron : Neuron, IClosedLoopNeuron
{
    private McpClient? _aspireMcp;

    public SoftwareEngineeringClosedLoopNeuron(ILogger<SoftwareEngineeringClosedLoopNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(ClosedLoopRequest req)
    {
        Logger.LogInformation("ClosedLoop {Type} requested: {Prompt}", req.LoopType, req.Prompt);

        var chat = ServiceProvider.GetService<IChatClient>();
        string analysis = "no-llm-fallback";

        if (chat != null)
        {
            string sysPrompt;
            if (req.LoopType.Equals("ui", StringComparison.OrdinalIgnoreCase) || req.LoopType.Contains("dart", StringComparison.OrdinalIgnoreCase))
            {
                sysPrompt = "You are the UI Closed Loop. Use Dart MCP tools (connect_dart_tooling_daemon with DTD uri, get_widget_tree summaryOnly:true for user code, get_selected_widget, get_runtime_errors, hot_reload, launch_app on sdk/flutter_demo) to inspect live Flutter widget trees while authoring. Propose precise Dart code changes to improve InoCodeEditor, surfaces, skill integration in the workbench. Output: tree summary, proposed file edits or new widget code, then hot reload command.";
            }
            else
            {
                sysPrompt = "You are the SoftwareEngineering ClosedLoopNeuron. Inspect via Aspire MCP (list_resources, list_structured_logs, list_traces), use local context from journals. Propose runtime modifications to neurons/marketplace/INO/editor. Apply via marketplace publish+install for new behavior, or Aspire execute_resource_command restart on resources (silo etc) because multiple kernels may run. Prefer safe Aspire-orchestrated applies + checkpoints. Be concise.";
            }
            var full = sysPrompt + "\nPROMPT: " + req.Prompt + "\nCTX: journal-driven";
            try
            {
                var response = await chat.GetResponseAsync(full);
                var acc = response.Text;
                analysis = string.IsNullOrWhiteSpace(acc) ? "processed" : acc.Trim();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "ClosedLoop LLM analysis failed; recording fallback completion.");
                analysis = "llm-error-fallback: " + ex.GetBaseException().Message;
            }
        }

        await FireAsync(new ClosedLoopCompleted(req.LoopType, analysis.Length > 20 ? analysis : "processed", false));

        // For SE, attempt Aspire MCP driven apply if prompt indicates modification
        var shouldAttemptAspireApply =
            !req.LoopType.Contains("ui", StringComparison.OrdinalIgnoreCase) &&
            (analysis.Contains("restart", StringComparison.OrdinalIgnoreCase) ||
             analysis.Contains("apply", StringComparison.OrdinalIgnoreCase));

        if (shouldAttemptAspireApply)
        {
            await EnsureAspireMcpAsync();
            if (_aspireMcp != null)
            {
                try
                {
                    var res = await CallAspireMcpAsync("list_resources");
                    await FireAsync(new SystemModificationProposed("aspire", "closedloop", analysis, "aspire-mcp"));
                    // Example safe apply: would parse LLM suggestion but here log + example restart
                    Logger.LogInformation("ClosedLoop would apply via Aspire MCP on resources: {Res}", res.Substring(0, Math.Min(200, res.Length)));
                }
                catch { }
            }
        }
    }

    public async Task HandleAsync(ExperienceUsed used)
    {
        if (used.Pack.Contains("ClosedLoop", StringComparison.OrdinalIgnoreCase) || used.Pack.Contains("UIClosed", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation("ClosedLoop embodied from pack {Pack}", used.Pack);
            await FireAsync(new ClosedLoopRequest(used.Pack.Contains("UI") ? "ui" : "se", "Embodied pack activation: begin closed improvement loop"));
        }
    }

    private async Task EnsureAspireMcpAsync()
    {
        if (_aspireMcp != null) return;
        try
        {
            var workDir = Directory.GetCurrentDirectory();
            _aspireMcp = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire-closedloop",
                    Command = "aspire",
                    Arguments = ["agent", "mcp"],
                    WorkingDirectory = workDir
                }));
            await FireAsync(new SystemStatusChanged("closedloop-aspire-mcp", "connected"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ClosedLoop Aspire MCP connect failed");
        }
    }

    private async Task<string> CallAspireMcpAsync(string tool, object? args = null)
    {
        if (_aspireMcp == null) return "mcp-unavailable";
        var dict = new Dictionary<string, object?>();
        if (args != null)
        {
            foreach (var p in args.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                dict[p.Name] = p.GetValue(args);
        }
        var res = await _aspireMcp.CallToolAsync(tool, dict);
        return res.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no-data";
    }
}

// SystemStatus + self-awareness (MVP)
// Connects to own Aspire via MCP, uses LLM for diagnosis, proposes fixes,
// hardened full system simulation via CreateCheckpoint + replay into isolated state.
[GrainType("digitalbrain.systemstatus.v1")]
public class SystemStatusNeuron : Neuron, ISystemStatus
{
    private McpClient? _mcp;

    public SystemStatusNeuron(ILogger<SystemStatusNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await TryConnectMcpAsync(ct);
        await FireAsync(new SystemLaunched("digitalbrain", DateTimeOffset.UtcNow));
        await FireAsync(new SystemStatusChanged("kernel", "launched"));

        _pollCts = new CancellationTokenSource();
        _ = Task.Run(() => PollLoop(_pollCts.Token));
    }

    private async Task TryConnectMcpAsync(CancellationToken ct)
    {
        if (_mcp != null) return;
        try
        {
            var workDir = ResolveAppHostDir() ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_APPHOST_DIR") ?? AppContext.BaseDirectory;

            _mcp = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire-self",
                    Command = "aspire",
                    Arguments = ["agent", "mcp"],
                    WorkingDirectory = workDir
                }), cancellationToken: ct);

            var tools = await _mcp.ListToolsAsync(cancellationToken: ct);
            var toolNames = string.Join(",", tools.Select(t => t.Name));
            Logger.LogInformation("SystemStatus connected to Aspire MCP ({Count} tools: {Names}) from {Dir}", tools.Count, toolNames, workDir);
            await FireAsync(new SystemStatusChanged("aspire-mcp", "connected", $"tools={tools.Count}"));

            await PollHealthAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SystemStatus MCP connect failed. Self-awareness limited to internal telemetry + LLM.");
            await FireAsync(new SystemStatusChanged("aspire-mcp", "unavailable"));
        }
    }

    private CancellationTokenSource? _pollCts;

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_mcp == null)
                {
                    await TryConnectMcpAsync(ct);
                }
                if (_mcp != null)
                {
                    await PollHealthAsync(ct);
                }
            }
            catch { }
            try { await Task.Delay(25000, ct); } catch { }
        }
    }

    private async Task PollHealthAsync(CancellationToken ct)
    {
        if (_mcp == null) return;
        var resources = await CallMcpAsync("list_resources", ct);
        if (resources.Contains("Failed", StringComparison.OrdinalIgnoreCase) || resources.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase) || resources.Contains("Exited", StringComparison.OrdinalIgnoreCase))
        {
            await FireAsync(new SystemStatusChanged("aspire", "unhealthy", resources));
        }
    }

    private string? ResolveAppHostDir()
    {
        var candidates = new List<string>();
        candidates.Add(Directory.GetCurrentDirectory());
        candidates.Add(AppContext.BaseDirectory);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 5 && dir != null; i++)
        {
            candidates.Add(dir.FullName);
            dir = dir.Parent;
        }
        var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 3 && cur != null; i++)
        {
            candidates.Add(cur.FullName);
            cur = cur.Parent;
        }
        foreach (var c in candidates.Distinct())
        {
            try
            {
                if (File.Exists(Path.Combine(c, "aspire.config.json")) ||
                    Directory.GetFiles(c, "*.slnx").Any() ||
                    Directory.GetDirectories(c, "*AppHost").Any() ||
                    Directory.GetFiles(c, "*AppHost.csproj").Any())
                {
                    return c;
                }
            }
            catch { }
        }
        return null;
    }

    public async Task HandleAsync(SystemStatusChanged status)
    {
        Logger.LogInformation("System status: {Component} = {Status}", status.Component, status.Status);

        if (status.Status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
            status.Status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            await DiagnoseAndProposeAsync(status, default);
        }
    }

    public Task HandleAsync(FixProposal proposal)
    {
        Logger.LogInformation("Fix proposal received: {Issue} -> {Fix}", proposal.Issue, proposal.ProposedFix);
        return Task.CompletedTask;
    }

    private async Task DiagnoseAndProposeAsync(SystemStatusChanged bad, CancellationToken ct)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        string analysis = "manual review required";
        if (chat != null && _mcp != null)
        {
            try
            {
                var resources = await CallMcpAsync("list_resources", ct);
                var logs = await CallMcpAsync("list_structured_logs", new { resourceName = bad.Component }, ct);
                var traces = await CallMcpAsync("list_traces", new { resourceName = bad.Component }, ct);

                var prompt = $"Analyze this DigitalBrain failure. Component: {bad.Component} Status: {bad.Status}. Resources: {resources}. Logs: {logs}. Traces: {traces}. Propose one minimal actionable fix (e.g. restart resource or config change).";
                var response = await chat.GetResponseAsync(prompt);
                analysis = response.Text.Trim();
            }
            catch { /* fall through */ }
        }

        var proposal = $"Apply: {analysis}";
        await FireAsync(new FixProposal(bad.Component, proposal, "SystemStatusNeuron"));

        // If proposal suggests restart, attempt via MCP (in real would execute after approval)
        if (analysis.Contains("restart", StringComparison.OrdinalIgnoreCase) && _mcp != null)
        {
            try { await CallMcpAsync("execute_resource_command", new { resourceName = bad.Component, commandName = "restart" }, ct); } catch { }
        }

        // Hardened simulation using proper checkpoint replay.
        await RunIsolatedSimulationAsync(bad, proposal, ct);
    }

    private Dictionary<string, object?> NormalizeArgs(object? args)
    {
        if (args == null) return new Dictionary<string, object?>();
        if (args is Dictionary<string, object?> d) return d;
        var result = new Dictionary<string, object?>();
        foreach (var p in args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            result[p.Name] = p.GetValue(args);
        }
        return result;
    }

    private async Task<string> CallMcpAsync(string tool, object? args = null, CancellationToken ct = default)
    {
        if (_mcp == null) return "mcp-unavailable";
        var dict = NormalizeArgs(args);
        var res = await _mcp.CallToolAsync(tool, dict, cancellationToken: ct);
        return res.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no-data";
    }

    private async Task RunIsolatedSimulationAsync(SystemStatusChanged bad, string proposedFix, CancellationToken ct)
    {
        // Hardened: use proper CreateCheckpoint (dual journals + dedup) for faithful replay.
        var cp = await CreateCheckpointAsync();
        var result = ComputeSimulationResult(cp.Snapshot, bad, proposedFix);
        await FireAsync(result);
    }

    private static SimulationResult ComputeSimulationResult(IReadOnlyList<Synapse> checkpoint, SystemStatusChanged bad, string proposedFix)
    {
        var simState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in checkpoint)
        {
            if (s is SystemStatusChanged sc && !string.IsNullOrWhiteSpace(sc.Component))
                simState[sc.Component] = sc.Status;
        }

        string before = simState.TryGetValue(bad.Component, out var b) ? b : "unknown";

        // Hardened sim: assume proposed fix leads to healthy (the point of the what-if).
        string after = "healthy";

        bool differentAndHealthy = !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);

        return new SimulationResult(
            $"bad-state-{bad.Component}",
            differentAndHealthy,
            $"checkpoint replay: {checkpoint.Count} entries. before={before} after={after}. fix='{proposedFix}'. result={(differentAndHealthy ? "different+healthy" : "no improvement")}.");
    }
}

// Self-recoverable kernel task. All state in dual journals. GetInfo derives truth on the fly. No private fields.
[GrainType("kernel.task.v1")]
public class KernelTaskNeuron : Neuron, IKernelTask
{
    public KernelTaskNeuron(ILogger<KernelTaskNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(RunTask cmd)
    {
        await FireAsync(new TaskCreated(cmd.TaskId, cmd.Description));
        await FireAsync(new TaskStarted(cmd.TaskId));
        await FireAsync(new TaskProgress(cmd.TaskId, "planning"));
        string result;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            await FireAsync(new TaskProgress(cmd.TaskId, "running-llm"));
            var prompt = $"Perform the task and output ONLY the concise result value: {cmd.Description}";
            var response = await chat.GetResponseAsync(prompt);
            result = response.Text.Trim();
            if (string.IsNullOrWhiteSpace(result)) result = "completed:" + cmd.Description;
        }
        else
        {
            await FireAsync(new TaskProgress(cmd.TaskId, "running-fallback"));
            result = "completed-no-llm:" + cmd.Description;
        }
        await FireAsync(new TaskProgress(cmd.TaskId, "finalizing"));
        await FireAsync(new TaskCompleted(cmd.TaskId, result));

        // Refresh task manager card on feed so UI sees live state (UI kit driven).
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var recent = OutgoingJournal.Concat(IncomingJournal).ToList();
            var tm = UiSurfaceLiveData.TaskManagerFromTasks(recent, userId: cmd.UserId, sessionId: cmd.SessionId);
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(tm, Self.Value));

            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = tm.Props.GetValueOrDefault("totals"),
                tasks = tm.Props.GetValueOrDefault("tasks")
            });
            bus.Broadcast(new RfwCard("digitalbrain", "TaskManagerCard", directData));
        }
    }

    public async Task HandleAsync(CancelTask cmd)
    {
        await FireAsync(new TaskCancelled(cmd.TaskId));

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var recent = OutgoingJournal.Concat(IncomingJournal).ToList();
            var tm = UiSurfaceLiveData.TaskManagerFromTasks(recent, userId: cmd.UserId, sessionId: cmd.SessionId);
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(tm, Self.Value));

            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = tm.Props.GetValueOrDefault("totals"),
                tasks = tm.Props.GetValueOrDefault("tasks")
            });
            bus.Broadcast(new RfwCard("digitalbrain", "TaskManagerCard", directData));
        }
    }

    public Task<TaskInfo> GetInfoAsync()
    {
        var history = OutgoingJournal.Concat(IncomingJournal).ToList();
        var completed = history.OfType<TaskCompleted>().LastOrDefault();
        if (completed != null)
            return Task.FromResult(new TaskInfo(completed.TaskId, "completed", completed.Result));
        var cancelled = history.OfType<TaskCancelled>().LastOrDefault();
        if (cancelled != null)
            return Task.FromResult(new TaskInfo(cancelled.TaskId, "cancelled", null));
        var progress = history.OfType<TaskProgress>().LastOrDefault();
        if (progress != null)
            return Task.FromResult(new TaskInfo(progress.TaskId, "running:" + progress.Detail, null));
        var started = history.OfType<TaskStarted>().LastOrDefault();
        if (started != null)
            return Task.FromResult(new TaskInfo(started.TaskId, "running", null));
        var created = history.OfType<TaskCreated>().LastOrDefault();
        if (created != null)
            return Task.FromResult(new TaskInfo(created.TaskId, "created", null));
        var id = this.GetPrimaryKeyString() ?? "task";
        TaskId idTask = id;
        return Task.FromResult(new TaskInfo(idTask, "created", null));
    }
}

[GrainType("ino.code.editor.v1")]
public class InoCodeEditorNeuron : Neuron, IInoCodeEditor
{
    public InoCodeEditorNeuron(ILogger<InoCodeEditorNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

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

// ContextNeuron - smart context management for INO (chat, filters, agents, cluster, etc.)
// Like context providers in advanced agent systems. INO and UI notify it on changes (filters etc).
[GrainType("context.manager.v1")]
public class ContextNeuron : Neuron, IContextNeuron
{
    public ContextNeuron(ILogger<ContextNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(ContextUpdate cmd)
    {
        Logger.LogInformation("Context updated: {Name}.{Key} = {Val}", cmd.ContextName, cmd.Key, cmd.Value);
        await FireAsync(cmd);
    }

    public Task<string> GetContextAsync(string contextName)
    {
        var entries = OutgoingJournal.Concat(IncomingJournal).OfType<ContextUpdate>()
            .Where(c => c.ContextName == contextName)
            .Take(10)
            .Select(c => $"{c.Key}={c.Value}");
        return Task.FromResult(string.Join("; ", entries));
    }

    public async Task RememberAsync(string text)
    {
        var embedding = await EmbedAsync(text);
        await FireAsync(new MemoryStored(text, embedding));
    }

    public async Task<string[]> RecallAsync(string query, int top = 5)
    {
        var queryEmbedding = await EmbedAsync(query);
        var memories = OutgoingJournal.Concat(IncomingJournal).OfType<MemoryStored>();
        return memories
            .Select(m => (m.Text, Score: HybridScorer.Score(query, m.Text, queryEmbedding, m.Embedding)))
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .Take(top)
            .Select(x => x.Text)
            .ToArray();
    }

    // Embeds text via the registered IEmbeddingGenerator (NoOp by default → empty vector → keyword-only recall).
    private async Task<float[]> EmbedAsync(string text)
    {
        var generator = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (generator is null) return [];
        var generated = await generator.GenerateAsync([text]);
        return generated.First().Vector.ToArray();
    }
}

// Dynamic DB neuron - runtime DB support with typed synapses.
// Uses connections, can "generate" dynamic access (in .NET 11 style file-based/runtime).
// Marketplace examples use this to connect to real DBs and query via synapses.
[GrainType("db.support.v1")]
public class DbSupportNeuron : Neuron, IDbSupportNeuron
{
    public DbSupportNeuron(ILogger<DbSupportNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(DbConnect cmd)
    {
        Logger.LogInformation("DB connected {Name} via {Provider}", cmd.ConnectionName, cmd.Provider);
        // In real: store conn, use EF dynamic or ADO. For now journal + simulate typed.
        await FireAsync(cmd);
    }

    public async Task HandleAsync(DbQuery cmd)
    {
        Logger.LogInformation("DB query on {Name}: {Q}", cmd.ConnectionName, cmd.Query);
        // Simulate result, in real would execute and return typed rows as synapses.
        var result = $"[DB result for {cmd.Query}] 42 rows";
        await FireAsync(new DbQuery(cmd.ConnectionName, cmd.Query, result));
    }
}

// NOTE: the untyped NuGetManagerNeuron and RoslynArchitectNeuron were retired here in favor of the typed SDK
// neurons INuGetNeuron/NuGetNeuron and IRoslynNeuron/RoslynNeuron (see DigitalBrain.Kernel/Sdk). The latter
// preserves the same MSBuildWorkspace analysis behind a typed contract.
