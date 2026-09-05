using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Memory;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GraphTriggerTests
{
    [Fact]
    public void SendRequiresTheNeuronToHandleTheSignal()
    {
        var send = typeof(NeuronReferenceExtensions).GetMethod(
            nameof(NeuronReferenceExtensions.SendAsync),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(send);

        var arguments = send.GetGenericArguments();
        Assert.Equal("TNeuron", arguments[0].Name);
        Assert.Equal("TSignal", arguments[1].Name);
        Assert.Contains(
            arguments[0].GetGenericParameterConstraints(),
            constraint => constraint.IsGenericType
                && constraint.GetGenericTypeDefinition() == typeof(IHandle<>));
    }

    [Fact]
    public void BehaviorsAndXAccountsHandleOnlyTheirSignals()
    {
        Assert.True(typeof(IHandle<SaveBehaviorScript>).IsAssignableFrom(typeof(IBehavior)));
        Assert.False(typeof(IHandle<PublishPost>).IsAssignableFrom(typeof(IBehaviors)));
        Assert.True(typeof(IHandle<PublishPost>).IsAssignableFrom(typeof(IXAccount)));
        Assert.False(typeof(IHandle<SaveBehaviorScript>).IsAssignableFrom(typeof(IXAccount)));
        Assert.True(typeof(IHandle<OpenSurface>).IsAssignableFrom(typeof(IUIRenderer)));
        Assert.True(typeof(IHandle<StoreVectorMemory>).IsAssignableFrom(typeof(IVectorMemory)));
        Assert.True(typeof(IHandle<SearchVectorMemory>).IsAssignableFrom(typeof(IVectorMemory)));
        Assert.True(typeof(IHandle<AgentRequest>).IsAssignableFrom(typeof(IAgent)));
        Assert.True(typeof(IHandle<AgentRequest>).IsAssignableFrom(typeof(IAssistant)));
        Assert.False(typeof(IHandle<AgentRequest>).IsAssignableFrom(typeof(IChat)));
    }
}
