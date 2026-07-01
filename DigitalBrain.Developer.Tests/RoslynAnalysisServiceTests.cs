using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class RoslynAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeSolutionAsync_Analyzes_Real_Solution()
    {
        var service = new RoslynAnalysisService();
        var solutionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Brain.slnx"));
        var result = await service.AnalyzeSolutionAsync(solutionPath);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
