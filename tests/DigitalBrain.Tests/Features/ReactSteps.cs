using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class ReactSteps(BrainSteps brain)
{
    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at (echo|flaky|sleepy) ""(.*)""")]
    public Task FireAtTyped(string from, string type, string body, string grainType, string name)
        => brain.FireCore(from, type, body, new NeuronId(grainType, name));

    [Given(@"flaky ""(.*)"" fails its first reaction")]
    public static void GivenFlaky(string name) => FixtureSwitches.FlakyFailuresLeft[name] = 1;

    [Given(@"sleepy ""(.*)"" is asleep")]
    public static void GivenAsleep(string name) => FixtureSwitches.Asleep[name] = true;

    [When(@"sleepy ""(.*)"" is awake")]
    public async Task WhenAwake(string name)
    {
        FixtureSwitches.Asleep[name] = false;

        // Touching the neuron activates it; activation resumes the drain.
        _ = await brain.Brain.Grains.GetGrain<INeuron>(new NeuronId("sleepy", name).ToGrainId()).ReadState();
    }

    [When(@"""(.*)"" waits up to (\d+) seconds for an incoming ""(\w+)""")]
    public Task WaitOne(string neuron, int seconds, string type) => WaitMany(neuron, seconds, 1, type);

    [When(@"""(.*)"" waits up to (\d+) seconds for (\d+) incoming ""(\w+)""")]
    public async Task WaitMany(string neuron, int seconds, int count, string type)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var read = await brain.Journal(neuron, JournalKind.Incoming);
            if (read.Delta.Count(d => d.Signal.Type == type) >= count)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"{neuron} did not receive {count} {type} within {seconds}s");
    }

    [Then(@"""(.*)"" incoming ""(\w+)"" bodies are (.*)$")]
    public async Task ThenBodies(string neuron, string type, string expected)
    {
        var bodies = (await brain.Journal(neuron, JournalKind.Incoming)).Delta
            .Where(d => d.Signal.Type == type)
            .Select(d => d.Signal.Body);
        Assert.Equal(expected, string.Join(", ", bodies));
    }

    [Then(@"the latest ""(.*)"" incoming entry has the same correlation as the latest ""(.*)"" outgoing entry")]
    public async Task ThenSameCorrelation(string receiver, string source)
        => Assert.Equal(
            (await brain.Journal(source, JournalKind.Outgoing)).Delta[^1].CorrelationId,
            (await brain.Journal(receiver, JournalKind.Incoming)).Delta[^1].CorrelationId);

    [Then(@"flaky ""(.*)"" reacted (\d+) times to sequence (\d+)")]
    public static void ThenReacted(string name, int times, long sequence)
        => Assert.Equal(times, FixtureSwitches.Reactions[$"{name}:{sequence}"]);

    [Then(@"(?:echo|flaky|sleepy) ""(.*)"" incoming journal contains ""(\w+)"" (\{.*\})$")]
    public async Task ThenTypedIncoming(string name, string type, string body)
    {
        // The grain type is inferred from the scenario; try all three fixture types.
        foreach (var grainType in new[] { "echo", "flaky", "sleepy" })
        {
            var read = await brain.Brain.Grains
                .GetGrain<INeuron>(new NeuronId(grainType, name).ToGrainId())
                .ReadJournal(JournalKind.Incoming, 0);
            if (read.Delta.Any(d => d.Signal.Type == type && d.Signal.Body == body))
            {
                return;
            }
        }

        Assert.Fail($"no {type} {body} on {name}");
    }
}
