using System.Collections.Generic;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class WidgetTreeHopBridgeTests
{
    [Fact]
    public void WidgetTree_hop_surface_carries_markers_and_keys_correlation_on_surfaceId()
    {
        var tree = new UiWidgetTree(DigitalBrain.Core.Ui.Screen, new Dictionary<string, object?>(),
            new List<UiWidgetTree> { new(DigitalBrain.Core.Ui.Text, new Dictionary<string, object?> { ["text"] = "hi" }) });
        var surface = UiSurface.ForExperienceHopTree("hello-world", "hello-world", "ask", tree);

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, "hello-world");

        Assert.Equal("WidgetTreeHost", card.RootWidget);
        Assert.Equal("ask", card.CorrelationId);

        using var doc = JsonDocument.Parse(card.DataJson);
        var root = doc.RootElement;
        Assert.Equal("hello-world/hello-world", root.GetProperty("activeExperience").GetString());
        Assert.Equal("ask", root.GetProperty("surfaceId").GetString());
        Assert.True(root.TryGetProperty("tree", out _));
    }

    [Fact]
    public void WidgetTree_hop_surface_carries_title_marker()
    {
        var tree = new UiWidgetTree(DigitalBrain.Core.Ui.Screen, new Dictionary<string, object?>());
        var surface = UiSurface.ForExperienceHopTree("ui-gallery", "ui-gallery", "inputs", tree, title: "Inputs");

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, "ui-gallery");

        using var doc = System.Text.Json.JsonDocument.Parse(card.DataJson);
        Assert.Equal("Inputs", doc.RootElement.GetProperty("title").GetString());
    }
}
