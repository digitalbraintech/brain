namespace DigitalBrain.Core;

// Author-facing fluent definition of an experience: an ordered set of named hops, the first of which is the entry.
public sealed class UiExperience
{
    public string Id { get; }
    public string Name { get; }
    internal List<UiHop> Hops { get; } = new();

    internal UiExperience(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public UiExperience Hop(string hopId, Action<UiHop> body)
    {
        var hop = new UiHop(hopId);
        body(hop);
        Hops.Add(hop);
        return this;
    }
}

// One hop: an ordered list of node factories. A factory may depend on the accumulated flow state
// (e.g. the greeting text uses the captured name), so the literal is computed at emit time, keeping the client dumb.
public sealed class UiHop
{
    public string Id { get; }
    internal List<Func<IReadOnlyDictionary<string, string>, UiWidgetTree>> Factories { get; } = new();

    internal UiHop(string id) => Id = id;

    public UiHop Text(string text)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Text, new Dictionary<string, object?> { ["text"] = text }));
        return this;
    }

    public UiHop Text(Func<IReadOnlyDictionary<string, string>, string> text)
    {
        Factories.Add(state => new UiWidgetTree(Ui.Text, new Dictionary<string, object?> { ["text"] = text(state) }));
        return this;
    }

    public UiHop TextField(string name, string placeholder = "")
    {
        Factories.Add(_ => new UiWidgetTree(Ui.TextField,
            new Dictionary<string, object?> { ["name"] = name, ["placeholder"] = placeholder }));
        return this;
    }

    public UiHop Button(string label, string goTo)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Button,
            new Dictionary<string, object?> { ["label"] = label, ["eventName"] = goTo }));
        return this;
    }

    public UiHop Panel(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Panel, new Dictionary<string, object?>(),
            inner.Factories.Select(factory => factory(state)).ToList()));
        return this;
    }

    public UiHop Checkbox(string name, string label)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Checkbox,
            new Dictionary<string, object?> { ["name"] = name, ["label"] = label }));
        return this;
    }

    public UiHop Switch(string name, string label)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Switch,
            new Dictionary<string, object?> { ["name"] = name, ["label"] = label }));
        return this;
    }

    public UiHop TextArea(string name, string placeholder = "")
    {
        Factories.Add(_ => new UiWidgetTree(Ui.TextArea,
            new Dictionary<string, object?> { ["name"] = name, ["placeholder"] = placeholder }));
        return this;
    }

    public UiHop Select(string name, IReadOnlyList<string> options, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Select, new Dictionary<string, object?>
        {
            ["name"] = name, ["options"] = options, ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop RadioGroup(string name, IReadOnlyList<string> options, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.RadioGroup, new Dictionary<string, object?>
        {
            ["name"] = name, ["options"] = options, ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop Slider(string name, double min = 0, double max = 1, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Slider, new Dictionary<string, object?>
        {
            ["name"] = name, ["min"] = min, ["max"] = max, ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop DateField(string name, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.DateField, new Dictionary<string, object?>
        {
            ["name"] = name, ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop Row(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Row, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Column(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Column, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Divider()
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Divider, new Dictionary<string, object?>()));
        return this;
    }

    public UiHop Header(string title)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Header, new Dictionary<string, object?> { ["title"] = title }));
        return this;
    }

    public UiHop Gap(double size = 16)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Gap, new Dictionary<string, object?> { ["size"] = size }));
        return this;
    }
}
