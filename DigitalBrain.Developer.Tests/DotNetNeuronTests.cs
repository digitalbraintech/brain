using DigitalBrain.TestKit;
using DigitalBrain.Developer;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class DotNetNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Reports_Sdk_Version()
    {
        var dotnet = _brain.Grain<IDotNetNeuron>("dotnet-test");
        var version = await dotnet.VersionAsync();
        Assert.Matches(@"\d+\.\d+", version);
    }
}
