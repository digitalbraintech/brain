namespace DigitalBrain.Core;

// Fired by the host (or UI layer) once the user has filled in all RequiredConfig fields for a pack.
// Secret values in Values must never be logged.
[GenerateSerializer]
public record ConfigurationProvided(
    [property: Id(0)] string PackName,
    [property: Id(1)] IReadOnlyDictionary<string, string> Values)
    : Synapse(nameof(ConfigurationProvided), DateTimeOffset.UtcNow);

// Maps a pack's RequiredConfig (generic PackConfigField list) to a ui: kit UiSurface the thin client renders.
// Text/Secret -> ui:TextField (Secret marks the field), Choice -> ui:Select, plus a submit ui:Button that
// round-trips a ConfigurationProvided carrying the field values for the pack.
public static class ConfigFormSurface
{
    public const string Kind = "pack-config-form";

    public static UiSurface Build(string packName, IReadOnlyList<PackConfigField> fields, string? emitter = null)
    {
        var fieldNodes = fields.Select(ToNode).ToList();
        fieldNodes.Add(new UiWidgetTree(Ui.Button, new Dictionary<string, object?>
        {
            ["label"] = "Save",
            ["eventName"] = nameof(ConfigurationProvided),
            ["synapseType"] = nameof(ConfigurationProvided),
            ["pack"] = packName
        }));

        var tree = new UiWidgetTree(
            Ui.Screen,
            new Dictionary<string, object?> { ["title"] = packName + " configuration" },
            new List<UiWidgetTree> { new(Ui.Column, new Dictionary<string, object?>(), fieldNodes) });

        return new UiSurface(Kind, new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "surface.pack-config." + packName.ToLowerInvariant(),
            [UiSurfaceKeys.Title] = packName + " configuration",
            [UiSurfaceKeys.RequiresInput] = true,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            [UiSurfaceKeys.Emitter] = emitter ?? packName,
            ["pack"] = packName,
            ["tree"] = tree
        });
    }

    private static UiWidgetTree ToNode(PackConfigField field)
    {
        if (field.Kind == PackConfigFieldKind.Choice)
        {
            return new UiWidgetTree(Ui.Select, FieldProps(field, extra: props =>
            {
                if (field.Choices is not null) props["items"] = field.Choices;
            }));
        }

        return new UiWidgetTree(Ui.TextField, FieldProps(field, extra: props =>
        {
            if (field.Kind == PackConfigFieldKind.Secret) props["secret"] = true;
        }));
    }

    private static Dictionary<string, object?> FieldProps(PackConfigField field, Action<Dictionary<string, object?>> extra)
    {
        var props = new Dictionary<string, object?>
        {
            ["label"] = field.Label,
            ["key"] = field.Key,
            ["name"] = field.Key
        };
        if (field.DependsOnKey is not null) props["dependsOnKey"] = field.DependsOnKey;
        if (field.DependsOnValue is not null) props["dependsOnValue"] = field.DependsOnValue;
        extra(props);
        return props;
    }
}
