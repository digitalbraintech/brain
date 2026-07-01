using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;

namespace DigitalBrain.Tests.Ui;

// Compiles a bundle's SHIPPED pack source in-process (same Roslyn/ALC path the kernel uses)
// and drives ExperienceSteps against it, so the fast model loop and the live-render E2E
// consume one source of truth. No browser, no Aspire.
public sealed class BundleHarness : IDisposable
{
    private readonly EmbodiedPack _pack;
    private readonly string _experiencePack;
    private readonly string _experienceId;

    public BundleHarness(string packCode, string pack, string experienceId)
    {
        _pack = new PackAlcEmbodier().Embody(pack, packCode);
        _experiencePack = pack;
        _experienceId = experienceId;
    }

    public BundleManifest? Manifest => _pack.GetBundleManifest();

    public UiSurface Trigger(string eventName, params (string key, string value)[] args)
    {
        var step = new ExperienceStep(
            _experiencePack, _experienceId, eventName,
            args.ToDictionary(a => a.key, a => a.value));

        var outputs = _pack.Handle(step);
        return outputs.OfType<UiSurface>().FirstOrDefault()
               ?? throw new InvalidOperationException($"No UiSurface emitted for step '{eventName}'.");
    }

    public UiWidgetTree GetTree(string eventName, params (string key, string value)[] args)
    {
        var surface = Trigger(eventName, args);
        if (surface.Props.TryGetValue("tree", out var t) && t is UiWidgetTree tree)
            return tree;

        throw new NotSupportedException($"Step '{eventName}' did not produce a widget tree.");
    }

    public void Dispose() => _pack.Dispose();
}
