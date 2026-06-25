using DigitalBrain.Core;

namespace DigitalBrain.Silo.Gateway;

public static class KernelSurfaceDemo
{
    public const string RequestType = "DigitalBrain.Kernel.SurfaceDemoRequested";
    public const string PackName = "SurfaceDemoPack";
    public const string Version = "1.0";
    public const string GeneratedNeuronKey = "generated-surfacedemopack";
    public const string ObservabilityNeuronKey = "kernel-observability";

    public const string PackCode = """
        public sealed class SurfaceDemoPack : DigitalBrain.Core.IPackBehavior
        {
            public string Respond(string input) => "surface-demo:" + input;

            public DigitalBrain.Core.PackManifest GetManifest() =>
                new(new[] { new DigitalBrain.Core.SynapseType(nameof(DigitalBrain.Core.DemoMessageSynapse)) });

            public bool CanHandle(DigitalBrain.Core.Synapse synapse) =>
                synapse is DigitalBrain.Core.DemoMessageSynapse;

            public System.Collections.Generic.IReadOnlyList<DigitalBrain.Core.Synapse> Handle(DigitalBrain.Core.Synapse synapse)
            {
                var message = (DigitalBrain.Core.DemoMessageSynapse)synapse;
                var props = new System.Collections.Generic.Dictionary<string, object?>
                {
                    [DigitalBrain.Core.UiSurfaceKeys.SurfaceId] = "surface-demo-pack",
                    [DigitalBrain.Core.UiSurfaceKeys.Emitter] = "SurfaceDemoPack",
                    [DigitalBrain.Core.UiSurfaceKeys.Title] = "Embodied pack live",
                    [DigitalBrain.Core.UiSurfaceKeys.Priority] = 100,
                    [DigitalBrain.Core.UiSurfaceKeys.RequiresInput] = false,
                    [DigitalBrain.Core.UiSurfaceKeys.Layout] = DigitalBrain.Core.UiSurfaceLayouts.Panel,
                    ["body"] = "Typed synapse handled: " + message.Text,
                    ["status"] = "journaled",
                    ["tone"] = "teal"
                };

                return new DigitalBrain.Core.Synapse[]
                {
                    new DigitalBrain.Core.UiSurface(DigitalBrain.Core.UiSurfaceKinds.TaskWindow, props)
                };
            }
        }
        """;

    public static NeuroPack SignedPack()
    {
        var (privateKey, publicKey) = PackSignatureVerifier.GenerateKeyPair();
        return PackSignatureVerifier.SignPack(
            new NeuroPack(PackName, Version, OwnerId: "kernel-demo", Code: PackCode),
            privateKey,
            publicKey);
    }

    public static UiSurface ActivityGraphSurface(
        string correlationId,
        string phase,
        IReadOnlyList<Synapse>? generatedTimeline = null)
    {
        var timeline = generatedTimeline ?? Array.Empty<Synapse>();
        var latestSurface = timeline.OfType<UiSurface>().LastOrDefault();
        var latestInput = timeline.OfType<DemoMessageSynapse>().LastOrDefault();
        var latestInstall = timeline.OfType<NeuroPackInstalled>().LastOrDefault();
        var latestTelemetry = timeline.OfType<NeuronTelemetry>().LastOrDefault();
        var observedSynapses = timeline
            .TakeLast(12)
            .Select(s => new Dictionary<string, object?>
            {
                ["type"] = s.Type,
                ["synapseId"] = s.SynapseId,
                ["causationId"] = s.CausationId,
                ["correlationId"] = s.CorrelationId,
                ["at"] = s.Timestamp
            })
            .ToArray();

        var nodes = new[]
        {
            Node("flutter-ui", "Flutter UI", "dynamic", 0.92, phase.Contains("request", StringComparison.OrdinalIgnoreCase)),
            Node("gateway", "GatewayService", "kernel", 0.88, true),
            Node("marketplace", "MarketplaceNeuron", "system", 0.70, latestInstall is not null),
            Node(GeneratedNeuronKey, "GeneratedNeuron", "dynamic", 0.86, latestInput is not null),
            Node("surface-pack", "SurfaceDemoPack", "ai", 0.82, latestSurface is not null),
            Node("journals", "Dual journals", "data", 0.76, timeline.Count > 0),
            Node("surface-bus", "HomeFeedBus", "system", 0.80, latestSurface is not null)
        };

        var edges = new[]
        {
            Edge("flutter-ui", "gateway", RequestType, "Send", correlationId, 0.95),
            Edge("gateway", "marketplace", nameof(PublishToMarketplace), "FireAsync", correlationId, 0.72),
            Edge("marketplace", GeneratedNeuronKey, nameof(NeuroPackInstalled), "DeliverAsync", correlationId, latestInstall is null ? 0.48 : 0.82),
            Edge("gateway", GeneratedNeuronKey, nameof(DemoMessageSynapse), "FireAsync", correlationId, latestInput is null ? 0.56 : 0.88),
            Edge(GeneratedNeuronKey, "surface-pack", "IPackBehavior.Handle", "Handle", correlationId, latestSurface is null ? 0.42 : 0.90),
            Edge("surface-pack", "journals", nameof(UiSurface), "FireAsync", correlationId, latestSurface is null ? 0.38 : 0.86),
            Edge("journals", "surface-bus", nameof(RfwCard), "Broadcast", correlationId, latestSurface is null ? 0.34 : 0.78),
            Edge("surface-bus", "flutter-ui", "WatchHomeFeed", "stream", correlationId, latestSurface is null ? 0.30 : 0.84)
        };

        var events = new List<IReadOnlyDictionary<string, object?>>
        {
            Event("phase", "gateway", phase, correlationId, latestTelemetry?.SynapseId),
            Event("pack", "surface-pack", latestSurface is null ? "waiting for UiSurface" : "UiSurface journaled", correlationId, latestSurface?.SynapseId)
        };
        events.AddRange(observedSynapses);

        return new UiSurface(
            UiSurfaceKinds.ActivityGraph,
            new Dictionary<string, object?>
            {
                ["surfaceId"] = "surface.kernel.live-observability",
                ["emitter"] = "digitalbrain.kernel.observability",
                ["title"] = "Live Brain Observability",
                ["priority"] = 120,
                ["requiresInput"] = false,
                ["layout"] = UiSurfaceLayouts.Panel,
                ["phase"] = phase,
                ["correlationId"] = correlationId,
                ["nodes"] = nodes,
                ["edges"] = edges,
                ["events"] = events.TakeLast(16).ToArray(),
                ["journaledSynapseIds"] = observedSynapses.Select(e => e["synapseId"]).ToArray()
            })
        {
            CorrelationId = correlationId,
            CausationId = latestSurface?.CausationId,
            SynapseId = "surface-live-observability-" + Guid.NewGuid().ToString("N")
        };
    }

    private static IReadOnlyDictionary<string, object?> Node(
        string id,
        string label,
        string domain,
        double activity,
        bool active) => new Dictionary<string, object?>
        {
            ["id"] = id,
            ["label"] = label,
            ["domain"] = domain,
            ["activity"] = active ? activity : Math.Min(activity, 0.46),
            ["active"] = active
        };

    private static IReadOnlyDictionary<string, object?> Edge(
        string from,
        string to,
        string type,
        string method,
        string correlationId,
        double value) => new Dictionary<string, object?>
        {
            ["from"] = from,
            ["to"] = to,
            ["type"] = type,
            ["method"] = method,
            ["correlationId"] = correlationId,
            ["value"] = value,
            ["at"] = DateTimeOffset.UtcNow
        };

    private static IReadOnlyDictionary<string, object?> Event(
        string type,
        string nodeId,
        string title,
        string correlationId,
        string? causationId) => new Dictionary<string, object?>
        {
            ["type"] = type,
            ["nodeId"] = nodeId,
            ["title"] = title,
            ["correlationId"] = correlationId,
            ["causationId"] = causationId,
            ["at"] = DateTimeOffset.UtcNow
        };
}
