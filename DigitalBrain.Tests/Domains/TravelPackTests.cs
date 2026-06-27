using DigitalBrain.Core;
using DigitalBrain.Tests.E2E.Packs;
using Xunit;

namespace DigitalBrain.Tests.Domains;

public class TravelPackTests
{
    static ExperienceStep Step(string ev, params (string, string)[] args) =>
        new("travel", "plan-trip", ev, args.ToDictionary(a => a.Item1, a => a.Item2));

    static UiSurface OnlySurface(IReadOnlyList<Synapse> outputs)
    {
        var surface = outputs.OfType<UiSurface>().Single();
        Assert.Equal(UiSurface.RfwKind, surface.Kind);
        return surface;
    }

    static string SurfaceId(UiSurface s) => (string)s.Props[UiSurfaceKeys.SurfaceId]!;
    static string Source(UiSurface s)
    {
        var json = (string)s.Props["dataJson"]!;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("source").GetString()!;
    }

    [Fact]
    public void Start_emits_intro_surface_with_weather_and_flights()
    {
        var pack = new TravelPackBehavior();
        var outputs = pack.Handle(Step("start", ("prompt", "plan a trip to Bali next month")));

        var surface = OnlySurface(outputs);
        Assert.Equal("travel-intro", SurfaceId(surface));
        var src = Source(surface);
        Assert.Contains("WEATHER", src);
        Assert.Contains("Bali", src);
        Assert.Contains("ExperienceStep", src);      // flight Select button wires a step
        Assert.Contains("flight.selected", src);
    }

    [Fact]
    public void Flight_selected_emits_hotels_surface()
    {
        var pack = new TravelPackBehavior();
        pack.Handle(Step("start", ("prompt", "plan a trip to Bali next month")));
        var outputs = pack.Handle(Step("flight.selected", ("flightId", "FL-001")));

        var surface = OnlySurface(outputs);
        Assert.Equal("travel-hotels", SurfaceId(surface));
        Assert.Contains("HOTEL", Source(surface));
        Assert.Contains("hotel.selected", Source(surface));
    }

    [Fact]
    public void Manifest_declares_experience_step_handling()
    {
        var pack = new TravelPackBehavior();
        Assert.Contains(pack.GetManifest().HandledSynapseTypes, t => t.Value == nameof(ExperienceStep));
        Assert.True(pack.CanHandle(Step("start")));
    }
}
