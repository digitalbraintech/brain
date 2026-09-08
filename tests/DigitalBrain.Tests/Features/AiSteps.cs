using DigitalBrain.AI;
using DigitalBrain.Mcp;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class AiSteps(BrainSteps brain, BrainWorld world)
{
    [Given("a running brain with AI")]
    public async Task GivenAi()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule)]),
            ConfigureSilo = silo =>
            {
                silo.Services.RemoveAll<IChatClient>();
                silo.Services.AddSingleton<IChatClient>(new ScriptedChatClient("pong"));
            },
        });

    [When(@"""(.*)"" waits up to (\d+) seconds for an incoming ""(\w+)""")]
    public async Task WaitIncoming(string neuron, int seconds, string type)
    {
        var ops = new BrainOperations(brain.Brain.Grains);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var read = await ops.ReadAsync(new(neuron, "incoming"));
            if (read.Incoming!.Entries.Any(entry => entry.Type == type))
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"No incoming {type} on {neuron} within {seconds}s.");
    }

    [Then(@"""(.*)"" incoming contains a ""(\w+)""")]
    public async Task ThenIncomingContainsType(string neuron, string type)
    {
        var read = await new BrainOperations(brain.Brain.Grains).ReadAsync(new(neuron, "incoming"));
        Assert.Contains(read.Incoming!.Entries, entry => entry.Type == type);
    }
}
