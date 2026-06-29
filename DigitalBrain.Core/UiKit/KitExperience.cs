namespace DigitalBrain.Core;

// Base for a typed-C# UI app authored against the ui: kit. Subclasses implement Define() with the fluent builder.
// The base owns the ExperienceStep state machine, accumulates flow state, and emits each hop as a widget-tree surface.
public abstract class KitExperience : IPackBehavior
{
    private readonly Dictionary<string, string> _state = new();
    private UiExperience? _definition;

    protected abstract UiExperience Define();

    protected static UiExperience Experience(string id, string name) => new(id, name);

    public string Respond(string input) => $"experience:{input}";

    public PackManifest GetManifest() => new(new[] { new SynapseType(nameof(ExperienceStep)) });

    public bool CanHandle(Synapse synapse) => synapse is ExperienceStep;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not ExperienceStep step) return Array.Empty<Synapse>();

        foreach (var (key, value) in step.Args) _state[key] = value;

        var experience = _definition ??= Define();
        if (experience.Hops.Count == 0) return Array.Empty<Synapse>();

        var hopId = step.EventName == "start" ? experience.Hops[0].Id : step.EventName;
        var hop = experience.Hops.FirstOrDefault(h => h.Id == hopId);
        if (hop is null) return Array.Empty<Synapse>();

        var screen = BuildScreen(hop, _state, experience.Id);
        return new Synapse[]
        {
            UiSurface.ForExperienceHopTree(experience.Id, experience.Id, hop.Id, screen, title: experience.Name, emitter: experience.Id)
        };
    }

    private static UiWidgetTree BuildScreen(UiHop hop, IReadOnlyDictionary<string, string> state, string id)
    {
        var children = hop.Factories.Select(factory => Inject(factory(state), id)).ToList();
        return new UiWidgetTree(Ui.Screen, new Dictionary<string, object?>(), children);
    }

    // Any action-bearing node (carries eventName) must know pack + experienceId so the client can route the ExperienceStep.
    // Hops do not know those ids, so the base injects them at emit time — covers Button, Tile, and future nav nodes.
    private static UiWidgetTree Inject(UiWidgetTree node, string id)
    {
        if (node.Props.ContainsKey("eventName"))
        {
            var props = new Dictionary<string, object?>(node.Props) { ["pack"] = id, ["experienceId"] = id };
            node = node with { Props = props };
        }
        if (node.Props.ContainsKey("items"))
        {
            var props = new Dictionary<string, object?>(node.Props) { ["pack"] = id, ["experienceId"] = id };
            node = node with { Props = props };
        }
        if (node.Children is { } children)
        {
            return node with { Children = children.Select(child => Inject(child, id)).ToList() };
        }
        return node;
    }
}
