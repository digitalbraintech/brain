using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class DotNetNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Reports_Sdk_Version()
    {
        var dotnet = Grain<IDotNetNeuron>("dotnet-test");
        var version = await dotnet.VersionAsync();
        Assert.Matches(@"\d+\.\d+", version);
    }
}
