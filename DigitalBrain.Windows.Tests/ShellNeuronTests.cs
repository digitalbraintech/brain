using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Windows.Tests;

public class ShellNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Executes_Echo()
    {
        var shell = _brain.Grain<IShellNeuron>("shell-test");
        var result = await shell.ExecuteAsync("echo digitalbrain");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("digitalbrain", result.Output);
    }

    [Fact]
    public async Task Blocks_Dangerous_Command()
    {
        var shell = _brain.Grain<IShellNeuron>("shell-block");
        var result = await shell.ExecuteAsync("format c:");
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
