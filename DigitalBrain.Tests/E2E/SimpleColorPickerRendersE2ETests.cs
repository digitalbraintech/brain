using DigitalBrain.Core;
using DigitalBrain.Tests.E2E.Packs;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class SimpleColorPickerRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    // SimpleColorPicker demo E2E render test removed (pack literal bloat deleted from Core).
}
