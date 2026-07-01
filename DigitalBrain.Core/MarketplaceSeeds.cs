namespace DigitalBrain.Core;

public static class MarketplaceSeeds
{
    // Single source of truth for the Telegram responder pack. The embodiment test references this const
    // directly so there is no duplicated copy of the pack source.
    public const string TelegramResponderPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class TelegramResponderNeuron : IPackBehavior
{
    // Marketplace NeuroPack name + scope this pack's config is stored under; the responder reads the
    // user's chosen LLM provider/key from (ConfigScope, ConfigPack) to route the AskLlm.
    private const string ConfigPack = "DigitalBrain.Telegram.Responder";
    private const string ConfigScope = "default";

    public PackManifest GetManifest() => new(
        new[] { new SynapseType(TelegramSignals.MessageReceived) },
        new PackConfigField[]
        {
            new("telegram_token", "Bot token",    PackConfigFieldKind.Secret),
            new("llm_provider",   "LLM",          PackConfigFieldKind.Choice, new[] { "ollama", "openai" }),
            new("llm_key",        "API key",       PackConfigFieldKind.Secret,
                DependsOnKey: "llm_provider", DependsOnValue: "openai"),
        });

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not Signal s || s.Name != TelegramSignals.MessageReceived)
            return System.Array.Empty<Synapse>();

        var text   = s.Props.TryGetValue("text",   out var t) ? t?.ToString() ?? "" : "";
        var chatId = s.Props.TryGetValue("chatId", out var c) ? c : null;

        return new Synapse[]
        {
            new AskLlm(
                text,
                TelegramSignals.ReplyRequested,
                new Dictionary<string, object?> { ["chatId"] = chatId },
                ConfigPack,
                ConfigScope)
        };
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Channel,
        null,
        new[] { BundleChannel.Telegram });
}
""";

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

    public static class HelloWorldHops
    {
        public const string Ask = "ask";
        public const string Greeting = "greeting";
    }

public const string SimpleColorPickerPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class SimpleColorPickerExperience : KitExperience
{
    protected override UiExperience Define() => Experience("simple-color-picker", "Simple Color Picker")
        .Hop("choose", s => s
            .Text("Pick your favorite color")
            .Select("color", new List<string> { "Red", "Green", "Blue" }, "Color")
            .Button("Show result", "result"))
        .Hop("result", s => s
            .Text(state => "You chose: " + (state.GetValueOrDefault("color") ?? "none"))
            .Button("Choose again", "choose"));
}
""";

    public static class SimpleColorPickerHops
    {
        public const string Choose = "choose";
        public const string Result = "result";
    }


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

    public static class UiGalleryHops
    {
        public const string Inputs = "inputs";
        public const string Display = "display";
        public const string Feedback = "feedback";
        public const string Navigation = "navigation";
        public const string Overlays = "overlays";
    }

    public const string KeywordWatcherPackCode = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class KeywordWatcherNeuron : IPackBehavior
{
    public PackManifest GetManifest() => new(
        new[] { new SynapseType(TelegramSignals.MessageReceived) },
        null);

    public string Respond(string input) => input;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not Signal s || s.Name != TelegramSignals.MessageReceived)
            return System.Array.Empty<Synapse>();

        var text = s.Props.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
        if (!text.StartsWith("remind me", System.StringComparison.OrdinalIgnoreCase))
            return System.Array.Empty<Synapse>();

        return new Synapse[]
        {
            new Signal("ReminderScheduled",
                new Dictionary<string, object?>
                {
                    ["chatId"]   = s.Props.TryGetValue("chatId", out var c) ? c : null,
                    ["reminder"] = text
                })
        };
    }

    public BundleManifest? GetBundleManifest() => new(
        BundleTier.Channel,
        null,
        new[] { BundleChannel.Telegram });
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
            "Preinstalled ForUI primitive kit for rendering DigitalBrain UiSurface contracts. Tier-1 changes require Flutter rebuild/restart.",
            Manifest: new(BundleTier.Content, null, new[] { BundleChannel.InApp })),

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
            "Local Flutter client integration pack. Keeps startup to one UI resource by default.",
            Manifest: new(BundleTier.Content, null, new[] { BundleChannel.InApp })),

        new NeuroPack(
            "DigitalBrain.Experience.GmailInsights",
            "0.1.0",
            "digitalbraintech",
            false,
            0.0,
            "",
            "Preinstalled Gmail insights experience. Retrieves the last 100 Gmail-shaped messages from the local connector/sample path, summarizes them with the local Ollama model, and emits a chart surface."),

        new NeuroPack(
            "DigitalBrain.Telegram.Responder",
            "1.0.0",
            "digitalbraintech",
            false,
            0.05,
            TelegramResponderPackCode,
            "Telegram bot responder: receives TelegramMessageReceived signals, emits AskLlm to the LLM layer, configurable for Ollama or OpenAI. Install via marketplace, supply token and LLM config."),

        new NeuroPack(
            "DigitalBrain.Telegram.KeywordWatcher",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            KeywordWatcherPackCode,
            "Keyword watcher: listens for TelegramMessageReceived, emits ReminderScheduled when the text starts with 'remind me'."),

        new NeuroPack(
            "hello-world",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            HelloWorldPackCode,
            "Hello World — the smallest ui: kit app: enter your name, press Greet, see a greeting."),

        new NeuroPack(
            "simple-color-picker",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            SimpleColorPickerPackCode,
            "Simple interactive example: pick a color with select, see result using state in text. Demonstrates basic ui: kit experience flow."),

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
