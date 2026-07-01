using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

/// <summary>
/// Thin helper for declarative rolling update surfaces (extracted from SystemNeurons for tidy logic).
/// All properties are UiSurface driven; hosts render.
/// </summary>
public static class SystemRollingSurfaces
{
    public static UiSurface CreateDrain(int replica, string version, string checkpointId)
    {
        var props = new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingDrain}-{replica}",
            [UiSurfaceKeys.Emitter] = "aspire-orchestrator", // will be stamped by caller
            [UiSurfaceKeys.Title] = $"Drain Replica {replica}/3",
            [UiSurfaceKeys.Priority] = 70 + replica,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["replica"] = replica,
            ["phase"] = "draining",
            ["version"] = version,
            ["checkpointId"] = checkpointId
        };
        return new UiSurface(KernelUiSurfaceKinds.RollingDrain, props);
    }

    public static UiSurface CreateVerify(int replica, string version, string phase, int lineageEvents)
    {
        var props = new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingVerify}-{replica}",
            [UiSurfaceKeys.Emitter] = "aspire-orchestrator",
            [UiSurfaceKeys.Title] = $"Verify Replica {replica}/3",
            [UiSurfaceKeys.Priority] = 70 + replica,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["replica"] = replica,
            ["phase"] = phase,
            ["version"] = version,
            ["lineageEvents"] = lineageEvents
        };
        return new UiSurface(KernelUiSurfaceKinds.RollingVerify, props);
    }

    public static UiSurface CreateRollback(int replica, string version, string checkpointId)
    {
        var props = new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingRollback}-{replica}",
            [UiSurfaceKeys.Emitter] = "aspire-orchestrator",
            [UiSurfaceKeys.Title] = $"Rollback at Replica {replica}/3",
            [UiSurfaceKeys.Priority] = 90,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["replica"] = replica,
            ["phase"] = "rolledback",
            ["version"] = version,
            ["checkpointId"] = checkpointId,
            ["reason"] = "verify-failed"
        };
        return new UiSurface(KernelUiSurfaceKinds.RollingRollback, props);
    }

    public static UiSurface CreateComplete(string version, string checkpointId, int lineageEvents)
    {
        var props = new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = $"{KernelUiSurfaceKinds.RollingComplete}-{version}",
            [UiSurfaceKeys.Emitter] = "aspire-orchestrator",
            [UiSurfaceKeys.Title] = "Kernel Rolling Update",
            [UiSurfaceKeys.Priority] = 80,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["version"] = version,
            ["strategy"] = "one-replica-at-a-time",
            ["checkpointId"] = checkpointId,
            ["status"] = "complete",
            ["replicasProcessed"] = 3,
            ["lineageEvents"] = lineageEvents
        };
        return new UiSurface(KernelUiSurfaceKinds.RollingComplete, props);
    }
}
