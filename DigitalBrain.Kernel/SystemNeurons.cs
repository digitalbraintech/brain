using DigitalBrain.Core;
using DigitalBrain.UiKit;
using Microsoft.Extensions.AI;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

public static class KernelPack
{
    public const string Name = "kernel";
    public const string DefaultVersion = "0.3.0";
    public const string Description = "Core kernel substrate. Pre-installed; updatable via marketplace with rolling replica support.";
}

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
            [UiSurfaceKeys.Title] = "Kernel Dashboard"
        };
        await FireAsync(new UiSurface(KernelUiSurfaceKinds.Dashboard, dashboardProps));
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



