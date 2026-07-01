using DigitalBrain.Core;
using DigitalBrain.UiKit;
using Microsoft.Extensions.AI;
using Microsoft.CodeAnalysis;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

public static class KernelPack
{
    public const string Name = "kernel";
    public const string DefaultVersion = "0.3.0";
    public const string Description = "Core kernel substrate. Pre-installed; updatable via marketplace with rolling replica support.";
}

[GenerateSerializer]
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);

[GrainType("digitalbrain.kernel.aspire.v1")]
public class AspireOrchestratorNeuron(ILogger<AspireOrchestratorNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IAspireNeuron, IHandle<PerformKernelSelfUpdate>
{
    public async Task HandleAsync(StartDistributedApp cmd)
    {
        Logger.LogInformation("Aspire starting app: {App}", cmd.AppName);
        await FireAsync(new DistributedAppStarted(cmd.AppName, Success: true, "started via neuro"));
        await FireAsync(new SystemStatusChanged("aspire", "started", cmd.AppName));

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

        var recentEvents = OutgoingJournal.Concat(IncomingJournal).ToList();
        var taskSurface = UiSurfaceLiveData.TaskManagerFromTasks(recentEvents);
        await FireAsync(taskSurface);

        var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
        await flutter.DeliverAsync(taskSurface.Stamp(Self, CurrentCause));

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
        {
            var card = UiSurfaceRfwBridge.FromUiSurface(taskSurface, Self.Value);
            bus.Broadcast(card);

            var directData = System.Text.Json.JsonSerializer.Serialize(new
            {
                totals = taskSurface.Props.GetValueOrDefault("totals"),
                tasks = taskSurface.Props.GetValueOrDefault("tasks")
            });
            bus.Broadcast(new RfwCard("digitalbrain", "TaskManagerCard", directData));
        }

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

        var publishedForStart = MarketplaceSeeds.LocalUiPacks;
        var marketList = UiSurfaceLiveData.MarketplaceListFromPacks(publishedForStart, Array.Empty<NeuroPack>());
        await FireAsync(marketList);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(marketList, Self.Value));
        }

        var marketTreeSurface = UiSurfaceLiveData.MarketplaceTreeSurface(
            publishedForStart, Array.Empty<NeuroPack>(), tierFilter: null, channelFilter: null,
            emitter: Self.Value, title: "Marketplace");
        await FireAsync(marketTreeSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(marketTreeSurface, Self.Value));
        }

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
                new DigitalBrain.Core.UiWidgetTree(DigitalBrain.Core.NeuronUiKit.Sidebar, new Dictionary<string, object?> { ["title"] = "DigitalBrain" },
                    new List<DigitalBrain.Core.UiWidgetTree>(BuildShellMenuItems())),
                new DigitalBrain.Core.UiWidgetTree("content", new Dictionary<string, object?>
                {
                    ["defaultView"] = UiSurfaceKinds.MarketplaceList
                })
            });

        var appShellSurface = DigitalBrain.Core.UiSurface.ForWidgetTree(mainShellTree, title: "Main Shell", emitter: "shell-main");
        await FireAsync(appShellSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(appShellSurface, Self.Value));
        }

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

        var preUpdateCheckpoint = await CreateCheckpointAsync();

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        var lineageCount = 0;

        for (int replica = 1; replica <= 3; replica++)
        {
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

            await FireAsync(new RestartResource("silo", IsRollingUpdate: true, TargetVersion: version, Strategy: $"replica-{replica}-of-3"));

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

[GrainType("digitalbrain.observability.v1")]
public class ObservabilityNeuron(ILogger<ObservabilityNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IObservabilityNeuron
{
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

[GrainType("digitalbrain.optimizer.v1")]
public class MetaOptimizerNeuron(ILogger<MetaOptimizerNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IMetaOptimizerNeuron
{
    public async Task HandleAsync(NeuronTelemetry telemetry)
    {
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



