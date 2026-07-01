using System.Runtime.CompilerServices;
using DigitalBrain.Core;

namespace DigitalBrain.Tests.Distribution;

public class PersonalAssistantPackCodeEmbeddedResourceTests
{
    [Fact]
    public void PersonalAssistantPackCode_Matches_Real_PersonalAssistantNeuron_Source()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(GetThisFileDirectory(), "..", ".."));
        var realSourcePath = Path.Combine(
            repoRoot, "DigitalBrain.Experience.PersonalAssistant", "PersonalAssistantNeuron.cs");

        var realSource = File.ReadAllText(realSourcePath);

        Assert.Equal(realSource, MarketplaceSeeds.PersonalAssistantPackCode);
    }

    [Fact]
    public void PersonalAssistantPackCode_Seed_Entry_Carries_The_Embedded_Source()
    {
        var seed = Assert.Single(MarketplaceSeeds.LocalUiPacks, p => p.Name == "DigitalBrain.Experience.PersonalAssistant");

        Assert.Equal(MarketplaceSeeds.PersonalAssistantPackCode, seed.Code);
    }

    private static string GetThisFileDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
