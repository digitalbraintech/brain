using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

public static class UiSurfaceRfwBridge
{
    private const string DefaultSource = """
        import digitalbrain;
        widget root = Panel(
          radius: 20.0,
          padding: 18.0,
          child: VStack(
            gap: 12.0,
            cross: "stretch",
            children: [
              HStack(
                between: true,
                children: [
                  HStack(
                    gap: 10.0,
                    children: [
                      GlowIcon(seed: 8, size: 18.0, tone: "teal", shapeHint: "orb"),
                      Text(text: data.title, variant: "title"),
                    ]
                  ),
                  Badge(text: data.status, tone: data.tone),
                ]
              ),
              Divider(),
              SectionLabel(text: data.kind),
              Text(text: data.body, variant: "dim"),
              Divider(),
              Text(text: data.footer, variant: "dim"),
            ]
          )
        );
        """;

    public static RfwCard FromUiSurface(UiSurface surface, string emitter)
    {
        // If the surface already carries a full RFW or widget tree definition, honor it directly.
        if (surface.Kind == UiSurface.RfwKind || surface.Props.ContainsKey("source") || surface.Props.ContainsKey("rfwSource"))
        {
            var lib = ValueOrDefault(surface, "libraryName", "digitalbrain");
            var root = ValueOrDefault(surface, "rootWidget", "root");
            var dataJson = surface.Props.TryGetValue("dataJson", out var dj) && dj is string s ? s
                : JsonSerializer.Serialize(surface.Props);
            var correlation = surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var sid) && sid is string sidStr && sidStr.Length > 0
                ? sidStr
                : surface.CorrelationId ?? surface.SynapseId;
            return new RfwCard(lib, root, dataJson) { CorrelationId = correlation };
        }

        if (surface.Kind == UiSurface.WidgetTreeKind && surface.Props.TryGetValue("tree", out var treeObj))
        {
            // For widget trees we still use a lightweight RFW wrapper that the client knows how to expand into ForUI + sub RFW.
            // Real power: neuron can emit ForRfw or ForWidgetTree and client renders the tree.
            var dataJson = JsonSerializer.Serialize(new { tree = treeObj, kind = surface.Kind });
            return new RfwCard("digitalbrain", "WidgetTreeHost", dataJson)
            {
                CorrelationId = surface.CorrelationId ?? surface.SynapseId
            };
        }

        var title = ValueOrDefault(surface, UiSurfaceKeys.Title, "Live embodied surface");
        var body = ValueOrDefault(surface, "body", "A typed C# pack emitted this UiSurface through the kernel.");
        var status = ValueOrDefault(surface, "status", "live");
        var tone = ValueOrDefault(surface, "tone", "teal");
        var source = ValueOrDefault(surface, "source", DefaultSource);

        var data = new Dictionary<string, object?>
        {
            ["source"] = source,
            ["title"] = title,
            ["body"] = body,
            ["status"] = status,
            ["tone"] = tone,
            ["kind"] = surface.Kind,
            ["footer"] = "emitter: " + ValueOrDefault(surface, UiSurfaceKeys.Emitter, emitter),
            ["surfaceId"] = ValueOrDefault(surface, UiSurfaceKeys.SurfaceId, surface.SynapseId)
        };

        foreach (var (key, value) in surface.Props)
        {
            data[key] = value;
        }

        return new RfwCard("digitalbrain", "root", JsonSerializer.Serialize(data))
        {
            CorrelationId = surface.CorrelationId ?? surface.SynapseId
        };
    }

    private static string ValueOrDefault(UiSurface surface, string key, string fallback) =>
        surface.Props.TryGetValue(key, out var value) && value is not null
            ? value.ToString() ?? fallback
            : fallback;
}
