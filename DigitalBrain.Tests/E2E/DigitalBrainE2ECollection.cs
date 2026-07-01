namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[CollectionDefinition(nameof(DigitalBrainE2ECollection))]
public sealed class DigitalBrainE2ECollection : ICollectionFixture<DigitalBrainBrowserFixture>
{
}
