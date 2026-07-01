using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Developer.Tests;

// Closes a pre-existing zero-coverage gap: NuGetNeuron had no test before this plan.
// INuGetNeuron has no SearchAsync member (ListPackages/ListOutdated/Restore/AddPackage only) —
// deviates from the brief's literal snapshot, which named a non-existent method; ListPackagesAsync
// against this project's own csproj is the equivalent zero-network smoke check.
public class NuGetNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task ListPackages_Returns_Zero_Exit_Code()
    {
        var nuget = Grain<INuGetNeuron>("nuget-test");
        var csprojPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "DigitalBrain.Developer", "DigitalBrain.Developer.csproj"));
        var result = await nuget.ListPackagesAsync(csprojPath);
        Assert.Equal(0, result.ExitCode);
    }
}
