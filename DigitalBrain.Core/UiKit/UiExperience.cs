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
}
