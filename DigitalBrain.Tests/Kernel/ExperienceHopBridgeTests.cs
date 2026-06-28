using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class ExperienceHopBridgeTests
{
    [Fact]
    public void Hop_marker_and_surface_id_survive_the_rfw_bridge()
    {
        var surface = UiSurface.ForExperienceHop(
            pack: "travel", experienceId: "plan-trip", surfaceId: "travel-hotels",
            libraryName: "digitalbrain", rootWidget: "root",
            dataJson: "{\"source\":\"import digitalbrain;\"}");

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, emitter: "kernel");

        Assert.Equal("travel-hotels", card.CorrelationId);
        Assert.Equal("digitalbrain", card.LibraryName);
        Assert.Equal("root", card.RootWidget);
        Assert.Contains("\"activeExperience\":\"travel/plan-trip\"", card.DataJson);
        Assert.Contains("\"surfaceId\":\"travel-hotels\"", card.DataJson);
    }
}
