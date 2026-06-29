using System.Collections.Generic;
using System.Linq;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Domains;

public class KitExperienceTests
{
    [Fact]
    public void ForExperienceHopTree_carries_tree_and_markers()
    {
        var tree = new UiWidgetTree(DigitalBrain.Core.Ui.Screen, new Dictionary<string, object?>());
        var surface = UiSurface.ForExperienceHopTree("hello-world", "hello-world", "ask", tree, title: "Hello World");

        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Same(tree, surface.Props["tree"]);
        Assert.Equal("hello-world/hello-world", surface.Props["activeExperience"]);
        Assert.Equal("hello-world", surface.Props["experienceId"]);
        Assert.Equal("ask", surface.Props[UiSurfaceKeys.SurfaceId]);
    }

    private sealed class GreetPack : KitExperience
    {
        protected override UiExperience Define() => Experience("hello-world", "Hello World")
            .Hop("ask", s => s
                .Text("What's your name?")
                .TextField("name", "Your name")
                .Button("Greet", "greeting"))
            .Hop("greeting", s => s
                .Panel(p => p.Text(state =>
                    $"Hello {(state.TryGetValue("name", out var n) && n.Length > 0 ? n : "World")}!")));
    }

    private static ExperienceStep Step(string eventName, params (string, string)[] args) =>
        new("hello-world", "hello-world", eventName, args.ToDictionary(a => a.Item1, a => a.Item2));

    [Fact]
    public void Start_emits_ask_hop_with_text_field_and_button()
    {
        var pack = new GreetPack();
        var outputs = pack.Handle(Step("start"));

        var surface = Assert.IsType<UiSurface>(Assert.Single(outputs));
        Assert.Equal("ask", surface.Props[UiSurfaceKeys.SurfaceId]);
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(DigitalBrain.Core.Ui.Screen, tree.Type);
        Assert.Collection(tree.Children!,
            n => Assert.Equal(DigitalBrain.Core.Ui.Text, n.Type),
            n => Assert.Equal(DigitalBrain.Core.Ui.TextField, n.Type),
            n =>
            {
                Assert.Equal(DigitalBrain.Core.Ui.Button, n.Type);
                Assert.Equal("greeting", n.Props["eventName"]);
                Assert.Equal("hello-world", n.Props["pack"]);          // injected at emit time
                Assert.Equal("hello-world", n.Props["experienceId"]);
            });
    }

    [Fact]
    public void Greeting_hop_bakes_captured_name_into_text()
    {
        var pack = new GreetPack();
        pack.Handle(Step("start"));
        var outputs = pack.Handle(Step("greeting", ("name", "Alice")));

        var surface = Assert.IsType<UiSurface>(Assert.Single(outputs));
        Assert.Equal("greeting", surface.Props[UiSurfaceKeys.SurfaceId]);
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        var panel = Assert.Single(tree.Children!);
        Assert.Equal(DigitalBrain.Core.Ui.Panel, panel.Type);
        var text = Assert.Single(panel.Children!);
        Assert.Equal("Hello Alice!", text.Props["text"]);
    }

    [Fact]
    public void Manifest_handles_experience_step_only()
    {
        var pack = new GreetPack();
        Assert.True(pack.CanHandle(Step("start")));
        Assert.Contains(pack.GetManifest().HandledSynapseTypes, t => t.Value == nameof(ExperienceStep));
    }
}
