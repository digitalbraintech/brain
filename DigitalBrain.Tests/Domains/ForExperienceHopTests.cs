using DigitalBrain.Core;

namespace DigitalBrain.Tests.Domains;

public class ForExperienceHopTests
{
    static string DataJson(UiSurface s) => (string)s.Props["dataJson"]!;

    [Fact]
    public void Injects_marker_into_wire_dataJson()
    {
        var surface = UiSurface.ForExperienceHop(
            pack: "travel", experienceId: "plan-trip", surfaceId: "travel-hotels",
            libraryName: "digitalbrain", rootWidget: "root",
            dataJson: "{\"source\":\"import digitalbrain;\"}");

        Assert.Equal(UiSurface.RfwKind, surface.Kind);
        var json = DataJson(surface);
        Assert.Contains("\"activeExperience\":\"travel/plan-trip\"", json);
        Assert.Contains("\"experienceId\":\"plan-trip\"", json);
        Assert.Contains("\"surfaceId\":\"travel-hotels\"", json);
        Assert.Contains("\"source\":", json); // original payload preserved
    }

    [Fact]
    public void Sets_correlation_and_top_level_props()
    {
        var surface = UiSurface.ForExperienceHop(
            pack: "travel", experienceId: "plan-trip", surfaceId: "travel-intro",
            libraryName: "digitalbrain", rootWidget: "root",
            dataJson: "{}", title: "Plan a trip", emitter: "travel");

        Assert.Equal("travel-intro", surface.Props[UiSurfaceKeys.SurfaceId]);
        Assert.Equal("travel/plan-trip", surface.Props["activeExperience"]);
        Assert.Equal("plan-trip", surface.Props["experienceId"]);
        Assert.Equal("digitalbrain", surface.Props["libraryName"]);
        Assert.Equal("root", surface.Props["rootWidget"]);
        Assert.Equal("Plan a trip", surface.Props[UiSurfaceKeys.Title]);
        Assert.Equal("travel", surface.Props[UiSurfaceKeys.Emitter]);
    }
}
