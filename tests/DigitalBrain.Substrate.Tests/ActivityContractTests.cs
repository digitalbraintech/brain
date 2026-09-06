using DigitalBrain.Abstractions.Neurons;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class ActivityContractTests
{
    [Fact]
    public void ActivitiesAreOrdinaryTypedNeurons()
    {
        var assembly = typeof(INeuron).Assembly;
        var activities = assembly.GetType("DigitalBrain.Abstractions.Neurons.IActivities");
        var source = assembly.GetType("DigitalBrain.Abstractions.Neurons.IActivitySource");
        Assert.NotNull(activities);
        Assert.NotNull(source);
        Assert.True(typeof(INeuron).IsAssignableFrom(activities));
        Assert.True(typeof(INeuron).IsAssignableFrom(source));
        Assert.Equal("activities", activities.GetField("DefaultInstanceName")!.GetRawConstantValue());
        Assert.Equal("execution", source.GetField("DefaultInstanceName")!.GetRawConstantValue());
    }
}
