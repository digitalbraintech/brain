using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class UiSurfaceRfwBridgeSurfaceIdTests
{
    [Fact]
    public void Rfw_surface_uses_surfaceId_prop_as_correlation_when_correlation_is_null()
    {
        var surface = new UiSurface(UiSurface.RfwKind, new Dictionary<string, object?>
        {
            ["libraryName"] = "digitalbrain",
            ["rootWidget"] = "root",
            ["dataJson"] = "{\"source\":\"import digitalbrain;\"}",
            [UiSurfaceKeys.SurfaceId] = "travel-hotels",
        });

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, "travel");

        Assert.Equal("travel-hotels", card.CorrelationId);
    }
}
