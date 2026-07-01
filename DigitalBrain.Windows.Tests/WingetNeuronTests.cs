using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Windows.Tests;

// Closes a pre-existing zero-coverage gap: WingetNeuron had no test before this plan.
// Only read-only operations (List/Search) run for real — Install/UpgradeAll mutate the host
// and are intentionally not exercised here.
public class WingetNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task List_Returns_Zero_Exit_Code()
    {
        var winget = _brain.Grain<IWingetNeuron>("winget-test");
        var result = await winget.ListAsync();
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Search_Returns_Zero_Exit_Code()
    {
        var winget = _brain.Grain<IWingetNeuron>("winget-search-test");
        var result = await winget.SearchAsync("git");
        Assert.Equal(0, result.ExitCode);
    }
}
