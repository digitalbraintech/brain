using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Signals;
namespace DigitalBrain.Chat;

public static class KitCardKinds
{
    public const string Chart = "chart";
    public const string Image = "image";
    public const string Spreadsheet = "spreadsheet";
    public const string Graph = "graph";
}

// A reference card: state lives in the named kit entity, never in the message.
[GenerateSerializer]
[Alias("ui.kit-card")]
public sealed record KitCardOffer(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Name,
    [property: Id(2)] string Caption) : Signal;
