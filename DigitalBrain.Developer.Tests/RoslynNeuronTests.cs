using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Developer.Tests;

// Closes a pre-existing zero-coverage gap: RoslynNeuron had no test before this plan.
// In-proc (MSBuildWorkspace), safe to run for real against the actual solution.
public class RoslynNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Analyzes_Real_Solution()
    {
        var roslyn = Grain<IRoslynNeuron>("roslyn-test");
        var solutionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Brain.slnx"));
        var result = await roslyn.AnalyzeSolutionAsync(solutionPath);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
