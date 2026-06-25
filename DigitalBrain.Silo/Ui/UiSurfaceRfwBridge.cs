using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Silo;

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
