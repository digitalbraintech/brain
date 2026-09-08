using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.AI;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class AiSteps(BrainWorld world, BrainSteps brain)
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

    [Given("the scripted model will time out")]
    public void GivenTimeOut() => world.Scripted.TimeOut();

    [Then(@"the scripted model was asked with model ""(.*)""")]
    public void ThenModelId(string model)
        => Assert.Contains(world.Scripted.Options, o => o?.ModelId == model);

    [Then(@"the scripted model's last request contained (\d+) user messages")]
    public void ThenLastRequestUserMessages(int count)
        => Assert.Equal(count, world.Scripted.Calls[^1].Count(m => m.Role == ChatRole.User));

    [Given("the scripted model will pause before its next answer")]
    public void GivenPause() => world.Scripted.Pause();

    [When("the scripted model is unpaused")]
    public void WhenUnpaused() => world.Scripted.Unpause();

    // A system prompt reaches a chat client as ChatOptions.Instructions, or as a system
    // message for a client that materialises it; either counts as the model having seen it.
    [Then(@"the scripted model received a system message ""(.*)""")]
    public void ThenSystemMessage(string text)
        => Assert.True(
            world.Scripted.Options.Any(o => o?.Instructions == text)
                || world.Scripted.Calls.Any(c => c.Any(m => m.Role == ChatRole.System && m.Text.Contains(text, StringComparison.Ordinal))),
            $"no request carried the system prompt '{text}'");

    [Then(@"the scripted model received a tool result containing ""(.*)""")]
    public void ThenToolResult(string fragment)
    {
        var results = world.Scripted.Calls
            .SelectMany(call => call)
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.Result?.ToString() ?? "")
            .ToList();
        Assert.True(
            results.Any(result => result.Contains(fragment, StringComparison.Ordinal)),
            $"no tool result contained '{fragment}'; results were: {string.Join(" | ", results)}");
    }

    [Then(@"the scripted model saw (\d+) conversations with (\d+) user message each")]
    public void ThenConversations(int conversations, int userMessages)
    {
        var calls = world.Scripted.Calls;
        Assert.Equal(conversations, calls.Count);
        Assert.All(calls, call => Assert.Equal(userMessages, call.Count(m => m.Role == ChatRole.User)));
    }

    [Then(@"the latest ""(.*)"" incoming ""(\w+)"" text contains ""(.*)""")]
    public async Task ThenLatestTextContains(string neuron, string type, string fragment)
    {
        var body = (await brain.Journal(neuron, JournalKind.Incoming)).Delta.Last(d => d.Signal.Type == type).Signal.Body;
        var text = (JsonNode.Parse(body) as JsonObject)?["text"]?.GetValue<string>() ?? string.Empty;
        Assert.Contains(fragment, text, StringComparison.Ordinal);
    }

    // The scripted client is the default provider, so nothing in a scenario names a model.
    private static Action<ISiloBuilder> Scripted(BrainWorld world)
        => silo =>
        {
            silo.Services.AddKeyedSingleton<IChatClient>("scripted", (_, _) => world.Scripted);
            silo.Services.AddSingleton(new AIDefaults("scripted"));
        };
}
