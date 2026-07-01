using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.TestKit.Tests;

public class NeuronTestBaseTests : NeuronTestBase
{
    [Fact]
    public async Task Grain_Resolves_And_Returns_A_Live_Timeline()
    {
        var target = Grain<IDemoNeuron>("neuron-test-base-smoke");
        var timeline = await target.GetTimelineAsync();
        Assert.NotNull(timeline);
    }
}
