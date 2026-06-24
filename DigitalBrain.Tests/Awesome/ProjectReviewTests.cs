using DigitalBrain.Silo;
using Xunit;

namespace DigitalBrain.Tests.Awesome;

public class ProjectReviewTests
{
    [Fact]
    public void Missing_Path_Returns_Honest_Error()
    {
        var outcome = ProjectReview.Analyze(@"Z:\nonexistent\path\that\does\not\exist");
        Assert.Equal(0, outcome.FileCount);
        Assert.Contains("does not exist on the kernel machine", outcome.Summary);
        Assert.Contains("kernel resolves paths locally", outcome.Report);
    }

    [Fact]
    public void Counts_Todos_Over_Temp_Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.cs"), "line1\nTODO one\nline3\n");
            File.WriteAllText(Path.Combine(dir, "b.cs"), "TODO two\nTODO three\n");

            var outcome = ProjectReview.Analyze(dir);
            Assert.True(outcome.FileCount >= 2);
            Assert.True(outcome.TodoCount >= 3);
            Assert.False(outcome.Truncated);
            Assert.Contains("TODOs", outcome.Report);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void File_Count_Cap_Truncates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pr-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (var i = 0; i < 120; i++)
                File.WriteAllText(Path.Combine(dir, $"f{i}.cs"), "// no todo\n");

            var outcome = ProjectReview.Analyze(dir);
            Assert.True(outcome.Truncated);
            Assert.True(outcome.FileCount <= 100);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
    }
}
