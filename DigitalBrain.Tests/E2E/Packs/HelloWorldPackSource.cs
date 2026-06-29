namespace DigitalBrain.Tests.E2E.Packs;

// The canonical Slice 0 demo: a whole UI app in one ~15-line file. Shared by the marketplace seed and the E2E.
// MUST use explicit usings — packs compile standalone via Roslyn/ALC against DigitalBrain.Core only.
public static class HelloWorldPackSource
{
    public const string Code = """
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
}
