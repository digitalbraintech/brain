using DigitalBrain.Kernel.Foundry;

namespace DigitalBrain.Tests.Foundry;

public sealed class FakeBuildRunner : IBuildRunner
{
    public bool NextResult { get; set; } = true;
    public string NextLog { get; set; } = "ok";
    public int Calls { get; private set; }

    public Task<BuildOutcome> VerifyBuildAsync(string moduleName, string source)
    {
        Calls++;
        return Task.FromResult(new BuildOutcome(NextResult, NextLog));
    }
}

public sealed class FakeResourceController : IResourceController
{
    public int Restarts { get; private set; }
    public Task RestartSiloAsync(string reason)
    {
        Restarts++;
        return Task.CompletedTask;
    }
}

public class FoundryFakesTests
{
    [Fact]
    public async Task FakeBuildRunnerHonorsConfiguredResult()
    {
        var runner = new FakeBuildRunner { NextResult = false, NextLog = "boom" };
        var outcome = await runner.VerifyBuildAsync("M", "src");
        Assert.False(outcome.Success);
        Assert.Equal("boom", outcome.Log);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task FakeResourceControllerCountsRestarts()
    {
        var controller = new FakeResourceController();
        await controller.RestartSiloAsync("test");
        Assert.Equal(1, controller.Restarts);
    }
}
