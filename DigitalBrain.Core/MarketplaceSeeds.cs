namespace DigitalBrain.Core;

public static class MarketplaceSeeds
{
    public const string HelloWorldPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class HelloWorldExperience : KitExperience
{
    protected override UiExperience Define() => Experience("hello-world", "Hello World")
        .Hop("ask", s => s
            .Text("What's your name?")
            .TextField("name", "Your name")
            .Button("Greet", "greeting"))
        .Hop("greeting", s => s
            .Panel(p => p.Text(state =>
                "Hello " + (state.GetValueOrDefault("name") is { Length: > 0 } n ? n : "World") + "!")));
}
""";


    public const string UiGalleryPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class UiGalleryExperience : KitExperience
{
    protected override UiExperience Define() => Experience("ui-gallery", "UI Kit Gallery")
        .Hop("inputs", s => Nav(s)
            .Heading("Inputs")
            .Panel(p => p.Text("TextField").TextField("name", "Your name"))
            .Panel(p => p.Text("Checkbox").Checkbox("agree", "I agree"))
            .Panel(p => p.Text("Switch").Switch("notify", "Notify me"))
            .Panel(p => p.Text("TextArea").TextArea("bio", "About you"))
            .Panel(p => p.Text("Select").Select("color", new List<string> { "Red", "Green", "Blue" }, "Color"))
            .Panel(p => p.Text("RadioGroup").RadioGroup("size", new List<string> { "S", "M", "L" }))
            .Panel(p => p.Text("Slider").Slider("level", 0, 10, "Level"))
            .Panel(p => p.Text("DateField").DateField("when", "When"))
            .Button("Next: Display", "display"))
        .Hop("display", s => Nav(s)
            .Heading("Display")
            .Panel(p => p.Heading("Heading widget").Text("Body text here").Badge("New"))
            .Panel(p => p.Icon("star").Avatar(fallback: "AB"))
            .Panel(p => p.List(l => l.Tile("First item", "subtitle A").Tile("Second item", "subtitle B")))
            .Panel(p => p.Row(r => r.Text("Row item A").Divider().Text("Row item B")))
            .Panel(p => p.Column(c => c.Gap(8).Header("Section").Gap(4).Text("Under header"))))
        .Hop("feedback", s => Nav(s)
            .Heading("Feedback")
            .Panel(p => p.Alert("Heads up", "an inline alert"))
            .Panel(p => p.Progress(0.6))
            .Panel(p => p.Spinner())
            .Panel(p => p.Tooltip("hint text", t => t.Text("hover me"))))
        .Hop("navigation", s => Nav(s)
            .Heading("Navigation")
            .Panel(p => p.Tabs(("Tab A", "inputs"), ("Tab B", "display")))
            .Panel(p => p.Breadcrumb(("Home", "inputs"), ("Gallery", "navigation")))
            .Panel(p => p.Pagination(3, "page-"))
            .Panel(p => p.BottomNav(("Home", "inputs"), ("Display", "display"), ("More", "feedback"))))
        .Hop("overlays", s => Nav(s)
            .Heading("Overlays")
            .Panel(p => p.Dialog(false, "Sample Dialog", d => d.Text("Dialog content.").Button("Close", "overlays")))
            .Panel(p => p.Sheet(false, "Sample Sheet", sh => sh.Text("Sheet content.")))
            .Toast("Hello from the gallery"));

    private static UiHop Nav(UiHop s) => s.Sidebar(
        ("Inputs", "inputs"),
        ("Display", "display"),
        ("Feedback", "feedback"),
        ("Navigation", "navigation"),
        ("Overlays", "overlays"));
}
""";

    public static IReadOnlyList<NeuroPack> LocalUiPacks { get; } =
    [
        new NeuroPack(
            "DigitalBrain.UIKit.ForUI",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Trusted Flutter primitive pack: ForUI theme, shell chrome, icons, cards, forms, sidebars, menus, popovers, tooltips, resizable workbench controls, and common UiSurface renderers.",
            "Preinstalled ForUI primitive kit for rendering DigitalBrain UiSurface contracts. Tier-1 changes require Flutter rebuild/restart."),

        new NeuroPack(
            "DigitalBrain.UI.Workbench",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Startup workbench experience: one canvas with panels for tasks, activity graph, marketplace, INO input, task windows, and timeline.",
            "Preinstalled dynamic Flutter workbench. Layout and surface props stream at runtime."),

        new NeuroPack(
            "DigitalBrain.UI.Graph3D",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Trusted graph primitive pack: compact neuron activity graph, expandable 3D/canvas graph view, ClusterActivity and ThreeDGraphUpdate renderers.",
            "Compact activity graph UI primitive pack for live DigitalBrain cluster state."),

        new NeuroPack(
            "DigitalBrain.UI.CreatorSurfaces",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Surface templates for generated software: user-input, task-window, editor, preview, review, and approval panels driven by UiSurface data.",
            "Reusable dynamic surface templates for software created inside DigitalBrain."),

        new NeuroPack(
            "DigitalBrain.UI.AspireFlutter",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "Aspire Flutter integration recipe: one default Flutter UI client resource, start/restart descriptors, and rebuild guidance for trusted primitive pack updates.",
            "Local Flutter client integration pack. Keeps startup to one UI resource by default."),

        new NeuroPack(
            "DigitalBrain.Experience.GmailInsights",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "",
            "Preinstalled Gmail insights experience. Retrieves the last 100 Gmail-shaped messages from the local connector/sample path, summarizes them with the local Ollama model, and emits a chart surface."),

        // Example of reusable packed integration (like Telegram bot): no logic in core, just published pack.
        new NeuroPack(
            "Telegram.Bot",
            "1.0",
            "digitalbraintech",
            false,
            0.05,
            "Packed Telegram bot integration. No core logic; install via marketplace, configure token, wires via synapses or gRPC. Reusable across brains.",
            "Installable Telegram bot experience. Aspire-executable or process pack for distribution and reuse."),

        new NeuroPack(
            "hello-world",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            HelloWorldPackCode,
            "Hello World — the smallest ui: kit app: enter your name, press Greet, see a greeting."),

        new NeuroPack(
            "ui-gallery",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            UiGalleryPackCode,
            "Browse every ui: component in one place."),

        // Dummy for dev testing of full typed-C# behavior pack flow (packaging + publish + share + install + embody).
        new NeuroPack(
            "Dummy.BehaviorPack",
            "1.0.0-dev",
            "digitalbraintech",
            false,
            0.10,
            """
public class DummyBehaviorPack : DigitalBrain.Core.IPackBehavior
{
    public string Respond(string input) => "Dummy responded to: " + input;
    public DigitalBrain.Core.PackManifest GetManifest() => new(new[] { new DigitalBrain.Core.SynapseType("ExperienceUsed") });
}
""",
            "Dummy behavior pack for testing marketplace distribution of typed C# packs during kernel/experience development.")
    ];

    public static IEnumerable<PublishToMarketplace> LocalUiPackPublishCommands() =>
        LocalUiPacks.Select(ToPublishCommand);

    public static PublishToMarketplace ToPublishCommand(NeuroPack pack) =>
        TrustedPublisher.SignPublishCommand(new(
            pack.Name,
            pack.Version,
            pack.Code,
            pack.OwnerId,
            pack.IsPrivate,
            pack.CommissionRate,
            pack.Description));

    /// <summary>
    /// For developer workflow: publish a new kernel version (triggers self-update on install).
    /// Packaging a kernel "version" in dev is primarily the version bump + description; actual binary ships via Aspire/container.
    /// </summary>
    public static PublishToMarketplace KernelPublishCommand(string version = null) =>
        new(
            PackName: "kernel",
            Version: version ?? "0.3.1-dev",
            Code: "",
            OwnerId: "digitalbraintech",
            IsPrivate: false,
            CommissionRate: 0.0,
            Description: "Core kernel substrate (dev build).");

    /// <summary>
    /// Dummy full typed-C# behavior pack for testing packaging → publish (share) → install → embody flow during development.
    /// The Code is the complete compilable source for a class implementing IPackBehavior.
    /// </summary>
    public static PublishToMarketplace DummyBehaviorPackPublish() =>
        new(
            PackName: "Dummy.DevPack",
            Version: "1.0.0-dev",
            Code: """
                public sealed class DummyDevPack : DigitalBrain.Core.IPackBehavior
                {
                    public string Respond(string input) => "[dev] handled: " + (input ?? "");
                }
                """,
            OwnerId: "digitalbraintech",
            IsPrivate: false,
            CommissionRate: 0.05,
            Description: "Dummy behavior pack used to validate marketplace distribution while developing on DigitalBrain.");
}
