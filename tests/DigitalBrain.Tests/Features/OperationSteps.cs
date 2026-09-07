using System.Diagnostics;
using DigitalBrain.Mcp;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class OperationSteps(BrainSteps brain)
{
    private FireResult? _fire;
    private ReadResult? _read;
    private JournalView? _timed;
    private Exception? _error;
    private List<string> _walk = [];

    private BrainOperations Ops => new(brain.Brain.Grains);

    [When(@"session ""(.*)"" fires ""([^""]+)"" (\{.*\}) at ""(.*)""")]
    public Task SessionFires(string session, string type, string body, string to)
        => Try(() => Ops.FireAsync(session, new(type, body, to)));

    [When(@"session ""(.*)"" fires ""(\w+)"" with a body of (\d+) bytes at ""(.*)""")]
    public Task SessionFiresBig(string session, string type, int bytes, string to)
        => Try(() => Ops.FireAsync(session, new(type, "{\"t\":\"" + new string('x', bytes - 8) + "\"}", to)));

    [When(@"session ""(.*)"" fires ""(\w+)"" not-json at ""(.*)""")]
    public Task SessionFiresNotJson(string session, string type, string to)
        => Try(() => Ops.FireAsync(session, new(type, "not json", to)));

    [When(@"session ""(.*)"" fires ""(\w+)"" with an empty body at ""(.*)""")]
    public Task SessionFiresEmpty(string session, string type, string to)
        => Try(() => Ops.FireAsync(session, new(type, "", to)));

    [When(@"""(.*)"" is read$")]
    public async Task Read(string neuron) => _read = await Ops.ReadAsync(new(neuron));

    [When(@"""(.*)"" is read for ""(\w+)""$")]
    public async Task ReadFor(string neuron, string what) => _read = await Ops.ReadAsync(new(neuron, what));

    [When(@"""(.*)"" is read (\d+) times$")]
    public async Task ReadMany(string neuron, int times)
    {
        for (var i = 0; i < times; i++)
        {
            _read = await Ops.ReadAsync(new(neuron));
        }
    }

    [When(@"the synapses of ""(.*)"" for ""(\w+)"" are followed")]
    public async Task Walk(string topic, string type)
    {
        var read = await Ops.ReadAsync(new(topic, "synapses"));
        _walk = [.. read.Synapses!.Where(s => s.Type == type).Select(s => s.To).Order(StringComparer.Ordinal)];
    }

    [When(@"""(.*)"" incoming is read after (\d+) with a (\d+) second timeout$")]
    public async Task TimedRead(string neuron, long after, int seconds)
        => _timed = (await Ops.ReadAsync(new(neuron, "incoming", after, seconds))).Incoming;

    [When(@"""(.*)"" incoming is read after (\d+) with a (\d+) second timeout while ""(.*)"" fires ""(\w+)"" (\{.*\}) at ""(.*)"" after (\d+) ms")]
    public async Task TimedReadWhileFiring(string neuron, long after, int seconds, string from, string type, string body, string to, int delayMs)
    {
        var firing = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            await Ops.FireAsync(from, new(type, body, to));
        });
        var watch = Stopwatch.StartNew();
        _timed = (await Ops.ReadAsync(new(neuron, "incoming", after, seconds))).Incoming;
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(seconds), "read did not return early on arrival");
        await firing;
    }

    [Then(@"the operation failed with a message containing ""(.*)""")]
    public void ThenFailed(string fragment)
    {
        Assert.NotNull(_error);
        Assert.Contains(fragment, _error.Message, StringComparison.Ordinal);
    }

    [Then(@"the fire result reports (\d+) delivered")]
    public void ThenDelivered(int count)
    {
        Assert.Null(_error);
        Assert.Equal(count, _fire!.Delivered);
    }

    [Then(@"reading ""(.*)"" shows a synapse to ""(.*)"" for ""(\w+)""")]
    public async Task ThenReadSynapse(string neuron, string to, string type)
        => Assert.Contains((await Ops.ReadAsync(new(neuron, "synapses"))).Synapses!, s => s.To == to && s.Type == type);

    [Then(@"reading ""(.*)"" shows latest ""(\w+)"" (\{.*\})$")]
    public async Task ThenReadLatest(string neuron, string type, string body)
        => Assert.Equal(body, (await Ops.ReadAsync(new(neuron, "state"))).State!.Single(s => s.Type == type).Body);

    [Then("the read has state, synapses, incoming and outgoing")]
    public void ThenAllViews()
    {
        Assert.NotNull(_read!.State);
        Assert.NotNull(_read.Synapses);
        Assert.NotNull(_read.Incoming);
        Assert.NotNull(_read.Outgoing);
    }

    [Then("the read has only synapses")]
    public void ThenOnlySynapses()
    {
        Assert.NotNull(_read!.Synapses);
        Assert.Null(_read.State);
        Assert.Null(_read.Incoming);
        Assert.Null(_read.Outgoing);
    }

    [Then(@"the walk visited ""(.*)""")]
    public void ThenWalk(string expected) => Assert.Equal(expected, string.Join(", ", _walk));

    [Then(@"the timed read returned (\d+) entries")]
    public void ThenTimed(int count) => Assert.Equal(count, _timed!.Entries.Count);

    private async Task Try(Func<Task<FireResult>> action)
    {
        _error = null;
        try
        {
            _fire = await action();
        }
        catch (Exception error)
        {
            _error = error;
            brain.RecordError(error);
        }
    }
}
