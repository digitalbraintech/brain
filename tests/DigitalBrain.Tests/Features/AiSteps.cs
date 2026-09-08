using DigitalBrain.AI;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Reqnroll;

namespace DigitalBrain.Tests;

[Binding]
public sealed class AiSteps(BrainWorld world)
{
    [Given("a running brain with AI")]
    public async Task GivenAi()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule)]),
            ConfigureSilo = Scripted(world),
        });

    [Given("a running brain with durable storage and AI")]
    public async Task GivenDurableAi()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule)]),
            PersistenceDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N")),
            ConfigureSilo = Scripted(world),
        });

    [Given(@"the scripted model will say ""(.*)""")]
    public void GivenSay(string text) => world.Scripted.Say(text);

    [Given(@"the scripted model will call tool ""(\w+)"" with (\{.*\}) then say ""(.*)""")]
    public void GivenCallToolThenSay(string tool, string arguments, string text)
    {
        world.Scripted.CallTool(tool, arguments);
        world.Scripted.Say(text);
    }

    [Given("the scripted model will pause before its next answer")]
    public void GivenPause() => world.Scripted.Pause();

    [When("the scripted model is unpaused")]
    public void WhenUnpaused() => world.Scripted.Unpause();

    // The scripted client is the default provider, so nothing in a scenario names a model.
    private static Action<ISiloBuilder> Scripted(BrainWorld world)
        => silo =>
        {
            silo.Services.AddKeyedSingleton<IChatClient>("scripted", (_, _) => world.Scripted);
            silo.Services.AddSingleton(new AIDefaults("scripted"));
        };
}
