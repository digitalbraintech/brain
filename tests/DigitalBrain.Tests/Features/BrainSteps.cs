using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class BrainSteps(BrainWorld world)
{
    private int _lastCount;
    private Exception? _lastError;

    [Given("a running brain")]
    public async Task GivenARunningBrain()
        => world.Simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });

    [Given("a running brain with durable storage")]
    public async Task GivenADurableBrain()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N")),
        });

    [Given(@"""(.*)"" is connected to ""(.*)"" for ""(.*)""")]
    [When(@"""(.*)"" is connected to ""(.*)"" for ""(.*)""")]
    public Task Connect(string from, string to, string type) => Neuron(from).Connect(Id(to), type);

    [Given(@"""(.*)"" is disconnected from ""(.*)"" for ""(.*)""")]
    [When(@"""(.*)"" is disconnected from ""(.*)"" for ""(.*)""")]
    public Task Disconnect(string from, string to, string type) => Neuron(from).Disconnect(Id(to), type);

    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\})$")]
    public Task Fire(string from, string type, string body) => FireCore(from, type, body, (NeuronId?)null);

    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at ""(.*)""")]
    public Task FireAt(string from, string type, string body, string to) => FireCore(from, type, body, to);

    [When("the silo restarts")]
    public Task Restart() => Brain.RestartSiloAsync();

    [Then(@"the fire reached (\d+) neurons")]
    public void ThenReached(int count)
    {
        Assert.Null(_lastError);
        Assert.Equal(count, _lastCount);
    }

    [Then(@"the fire was rejected with a message containing ""(.*)""")]
    public void ThenRejected(string fragment)
    {
        Assert.NotNull(_lastError);
        Assert.Contains(fragment, Flatten(_lastError).Message, StringComparison.Ordinal);
    }

    [Then(@"""(.*)"" (incoming|outgoing) journal contains ""(\w+)"" (\{.*\})$")]
    public async Task ThenJournalContains(string name, string kind, string type, string body)
        => Assert.Contains((await Journal(name, Kind(kind))).Delta, d => d.Signal.Type == type && d.Signal.Body == body);

    [Then(@"""(.*)"" incoming journal is empty")]
    public async Task ThenIncomingEmpty(string name) => Assert.Empty((await Journal(name, JournalKind.Incoming)).Delta);

    [Then(@"""(.*)"" (incoming|outgoing) journal has (\d+) entries")]
    public async Task ThenJournalCount(string name, string kind, int count)
    {
        var read = await Journal(name, Kind(kind));
        Assert.Equal(count, read.ResetSnapshot?.RetainedCount ?? read.Delta.Count);
    }

    [Then(@"the latest ""(.*)"" incoming entry has source ""(.*)""")]
    public async Task ThenLatestSource(string name, string source)
        => Assert.Equal(Id(source), (await Journal(name, JournalKind.Incoming)).Delta[^1].Source);

    [Then(@"the latest ""(.*)"" incoming entry has the same signal id and correlation as the latest ""(.*)"" outgoing entry")]
    public async Task ThenSameEnvelope(string receiver, string source)
    {
        var incoming = (await Journal(receiver, JournalKind.Incoming)).Delta[^1];
        var outgoing = (await Journal(source, JournalKind.Outgoing)).Delta[^1];
        Assert.Equal(outgoing.SignalId, incoming.SignalId);
        Assert.Equal(outgoing.CorrelationId, incoming.CorrelationId);
    }

    [Then(@"""(.*)"" has a synapse to ""(.*)"" for ""(.*)""")]
    public async Task ThenHasSynapse(string from, string to, string type)
        => Assert.Contains(await Query(from).ReadSynapses(), s => s.Target == Id(to) && s.SignalType == type);

    [Then(@"""(.*)"" has (\d+) synapses")]
    public async Task ThenSynapseCount(string name, int count) => Assert.Equal(count, (await Query(name).ReadSynapses()).Count);

    [Then(@"""(.*)"" state is empty")]
    public async Task ThenStateEmpty(string name) => Assert.Empty(await Query(name).ReadState());

    [AfterScenario]
    public async Task AfterScenario()
    {
        if (world.Simulation is { } simulation)
        {
            await simulation.DisposeAsync();
            world.Simulation = null;
        }
    }

    // ---- helpers shared with later features ----

    internal BrainSimulation Brain => world.Brain;
    internal static NeuronId Id(string name)
        => NeuronId.TryParse(name, out var id) ? id : NeuronId.Plain(name);
    internal INeuron Neuron(string name) => Brain.Grains.GetGrain<INeuron>(Id(name).ToGrainId());
    internal INeuron Query(string name) => Brain.Grains.GetGrain<INeuron>(Id(name).ToGrainId());
    internal Task<JournalRead> Journal(string name, JournalKind kind) => Query(name).ReadJournal(kind, 0);
    internal static JournalKind Kind(string text) => text == "incoming" ? JournalKind.Incoming : JournalKind.Outgoing;

    internal Task FireCore(string from, string type, string body, string? to)
        => FireCore(from, type, body, to is null ? null : Id(to));

    internal async Task FireCore(string from, string type, string body, NeuronId? to)
    {
        _lastError = null;
        try
        {
            _lastCount = (await Neuron(from).Fire(Signal.Create(type, body), to, null)).Delivered;
        }
        catch (Exception error)
        {
            _lastError = error;
        }
    }

    internal void RecordError(Exception error) => _lastError = error;

    private static Exception Flatten(Exception error) => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions[0] : error;
}
