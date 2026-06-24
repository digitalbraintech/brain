using DigitalBrain.Protocol;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Awesome;

public class SoftwareEngineeringReviewerTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task Reviewer_Reviews_Project_And_Emits_Typed_Result()
    {
        var dir = Path.Combine(Path.GetTempPath(), "se-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "a.cs"), "class A {}\n// TODO fix me\n");

            var reviewer = _cluster!.GrainFactory.GetGrain<ISoftwareEngineeringReviewer>("se-reviewer");
            await reviewer.FireAsync(new ReviewProjectRequest(dir));

            var result = (await reviewer.GetTimelineAsync()).OfType<ReviewResult>().LastOrDefault();
            Assert.NotNull(result);
            Assert.True(result!.FileCount >= 1);
            Assert.True(result.TodoCount >= 1);
            Assert.Contains("# Review:", result.Report);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    [Fact]
    public async Task Reviewer_Reviews_Content_Diff()
    {
        var reviewer = _cluster!.GrainFactory.GetGrain<ISoftwareEngineeringReviewer>("se-content");
        await reviewer.FireAsync(new ReviewRequest("PR-42", "some code\nTODO write tests\n"));

        var result = (await reviewer.GetTimelineAsync()).OfType<ReviewResult>().LastOrDefault();
        Assert.NotNull(result);
        Assert.Equal("PR-42", result!.Target);
        Assert.Equal(1, result.TodoCount);
    }
}
