using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleSurfaceFacts
{
    [Fact]
    public void ModuleNeuronsExposeOnlyINeuron()
    {
        var extras = typeof(AIModule).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(Neuron)))
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.Namespace?.StartsWith("DigitalBrain", StringComparison.Ordinal) == true
                    && contract != typeof(INeuron)
                    && contract != typeof(INeuronQuery))
                .Select(contract => $"{type.Name}:{contract.Name}"))
            .ToArray();

        Assert.Empty(extras);
    }
}
