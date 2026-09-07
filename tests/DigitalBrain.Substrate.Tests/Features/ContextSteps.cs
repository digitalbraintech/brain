using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[Binding]
public sealed class ContextSteps(BrainWorld world)
{
    private Pong? _pong;
    private Exception? _error;
    private string? _temp;

    [Given("a running brain with durable storage")]
    public async Task GivenDurableBrain()
    {
        _temp = Path.Combine(Path.GetTempPath(), "db-ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = _temp,
        });
    }

    [Given(@"echo ""(.*)"" can handle Ping")]
    public Task GivenEcho(string name)
        => Echo(name).HandledCount();

    [Given(@"composer ""(.*)"" can handle Compose")]
    public Task GivenComposer(string name)
        => Composer(name).LastExecution();

    [Given(@"board ""(.*)"" can handle Notice")]
    public Task GivenBoard(string name)
        => Query(BoardId(name)).ReadJournal(JournalKind.Incoming, 0);

    [Given(@"board ""(.*)"" subscribes to composer ""(.*)"" for Notice")]
    public Task GivenBoardSubscribes(string board, string composer)
        => world.Brain.Grains.GetGrain<IBoard>(BoardId(board).ToGrainId())
            .HandleAsync(new Subscribe(ComposerId(composer), nameof(Notice)), CancellationToken.None);

    [Given(@"composer ""(.*)"" publishes without completing")]
    public Task GivenPublishWithoutComplete(string name)
        => Composer(name).Use(ComposerScript.PublishWithoutComplete);

    [Given(@"composer ""(.*)"" holds the echo result before completing")]
    public Task GivenHoldEcho(string name)
        => Composer(name).Use(ComposerScript.HoldEchoBeforeComplete);

    [When(@"composer ""(.*)"" is asked to compose ""(.*)""")]
    public async Task WhenCompose(string name, string text)
    {
        _error = null;
        _pong = null;
        try
        {
            _pong = await world.Brain.Brain.Get<IComposer>(name)
                .RequestAsync(new Compose(text));
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }

    [When(@"composer ""(.*)"" sends ping ""(.*)"" to echo ""(.*)""")]
    public async Task WhenSendToEcho(string composer, string text, string echo)
    {
        await Composer(composer).Use(ComposerScript.SendPing);
        await Composer(composer).Aim("echo", echo);
        await WhenCompose(composer, text);
        if (_error is null)
        {
            _ = _pong;
        }
    }

    [When(@"composer ""(.*)"" sends ping ""(.*)"" to composer ""(.*)""")]
    public async Task WhenSendToComposer(string composer, string text, string other)
    {
        await Composer(other).LastExecution();
        await Composer(composer).Use(ComposerScript.SendPing);
        await Composer(composer).Aim("composer", other);
        await WhenCompose(composer, text);
    }

    [When(@"composer ""(.*)"" publishes notice ""(.*)""")]
    public async Task WhenPublish(string composer, string text)
    {
        await Composer(composer).Use(ComposerScript.PublishNotice);
        await WhenCompose(composer, text);
    }

    [When(@"echo ""(.*)"" completes ping ""(.*)"" twice with the same pong")]
    public async Task WhenEchoTwice(string echo, string text)
    {
        await Echo(echo).RepeatIdenticalComplete();
        _pong = await world.Brain.Brain.Get<IEcho>(echo).RequestAsync(new Ping(text));
    }

    [When(@"echo ""(.*)"" then completes ping ""(.*)"" with a different pong")]
    public async Task WhenEchoConflict(string echo, string text)
    {
        _error = null;
        try
        {
            await Echo(echo).ConflictComplete(text);
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }

    [When("the silo restarts")]
    public Task WhenRestart()
        => world.Brain.RestartSiloAsync();

    [When("the unfinished compose is retried")]
    public async Task WhenRetry()
    {
        _error = null;
        _pong = null;
        try
        {
            await Composer("main").RetryUnfinished();
            _pong = await world.Brain.Brain.Get<IComposer>("main")
                .RequestAsync(new Compose("hello"));
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }

    [Then(@"the compose reply text is ""(.*)""")]
    [Then(@"the ping reply text is ""(.*)""")]
    public void ThenReply(string text)
    {
        Assert.Null(_error);
        Assert.Equal(text, _pong?.Text);
    }

    [Then("the send is handled")]
    public void ThenSendHandled()
        => Assert.Null(_error);

    [Then("the send fails")]
    public void ThenSendFails()
    {
        Assert.NotNull(_error);
        Assert.DoesNotContain("No execution context is bound.", _error.ToString(), StringComparison.Ordinal);
    }

    [Then("the compose fails as incomplete")]
    public void ThenIncomplete()
    {
        Assert.NotNull(_error);
        Assert.Contains("Incomplete", _error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Then("the conflicting complete is rejected")]
    public void ThenConflict()
        => Assert.NotNull(_error);

    [Then(@"the ping to echo ""(.*)"" and the compose on composer ""(.*)"" share a correlation")]
    public async Task ThenSameCorrelation(string echo, string composer)
    {
        var ping = await LatestIncoming<Ping>(EchoId(echo));
        var compose = await LatestIncoming<Compose>(ComposerId(composer));
        Assert.Equal(compose.CorrelationId, ping.CorrelationId);
    }

    [Then("those two deliveries have different signal ids")]
    public async Task ThenDifferentSignalIds()
    {
        var ping = await LatestIncoming<Ping>(EchoId("echo"));
        var compose = await LatestIncoming<Compose>(ComposerId("main"));
        Assert.NotEqual(compose.SignalId, ping.SignalId);
    }

    [Then(@"composer ""(.*)"" has a learned Ping synapse to echo ""(.*)""")]
    public async Task ThenLearnedPing(string composer, string echo)
    {
        var synapse = await Synapse(ComposerId(composer), EchoId(echo), nameof(Ping));
        Assert.NotNull(synapse);
        Assert.Equal(DigitalBrain.Abstractions.Synapses.SynapseKind.Learned, synapse.Value.Kind);
    }

    [Then(@"composer ""(.*)"" has no Ping synapse to composer ""(.*)""")]
    public async Task ThenNoPingSynapse(string source, string target)
        => Assert.Null(await Synapse(ComposerId(source), ComposerId(target), nameof(Ping)));

    [Then(@"composer ""(.*)"" has a bound Notice synapse to board ""(.*)""")]
    public async Task ThenBoundNotice(string composer, string board)
    {
        var synapse = await Synapse(ComposerId(composer), BoardId(board), nameof(Notice));
        Assert.NotNull(synapse);
        Assert.Equal(DigitalBrain.Abstractions.Synapses.SynapseKind.Bound, synapse.Value.Kind);
    }

    [Then(@"board ""(.*)"" incoming journal contains Notice ""(.*)""")]
    public async Task ThenNotice(string board, string text)
        => Assert.Contains(text, await IncomingNotices(BoardId(board)));

    [Then(@"board ""(.*)"" incoming journal does not contain Notice ""(.*)""")]
    public async Task ThenNoNotice(string board, string text)
        => Assert.DoesNotContain(text, await IncomingNotices(BoardId(board)));

    [Then("the echo execution is a child of the compose execution")]
    public async Task ThenChild()
    {
        var parent = await Composer("main").LastExecution();
        var child = await Echo("echo").LastExecution();
        Assert.NotNull(parent);
        Assert.NotNull(child);
        Assert.Equal(parent.ExecutionId, child.ParentExecutionId);
    }

    [Then("those executions share a correlation")]
    public async Task ThenExecutionsShareCorrelation()
    {
        var parent = await Composer("main").LastExecution();
        var child = await Echo("echo").LastExecution();
        Assert.Equal(parent!.CorrelationId, child!.CorrelationId);
    }

    [Then("the echo execution has a distinct execution id")]
    public async Task ThenDistinctExecution()
    {
        var parent = await Composer("main").LastExecution();
        var child = await Echo("echo").LastExecution();
        Assert.NotEqual(parent!.ExecutionId, child!.ExecutionId);
    }

    [Then(@"echo ""(.*)"" handled Ping once")]
    public async Task ThenHandledOnce(string echo)
        => Assert.Equal(1, await Echo(echo).HandledCount());

    [Then(@"echo ""(.*)"" handled Ping (\d+) times")]
    public async Task ThenHandledTimes(string echo, int count)
        => Assert.Equal(count, await Echo(echo).HandledCount());

    [AfterScenario]
    public void AfterScenario()
    {
        if (_temp is not null && Directory.Exists(_temp))
        {
            try { Directory.Delete(_temp, true); }
            catch (IOException) { }
        }
    }

    private IEcho Echo(string name)
        => world.Brain.Grains.GetGrain<IEcho>(EchoId(name).ToGrainId());

    private IComposer Composer(string name)
        => world.Brain.Grains.GetGrain<IComposer>(ComposerId(name).ToGrainId());

    private INeuronQuery Query(NeuronId id)
        => world.Brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

    private static NeuronId EchoId(string name)
        => NeuronId.For<IEcho>(new OwnerId(DigitalBrainNames.DefaultOwner), name);

    private static NeuronId ComposerId(string name)
        => NeuronId.For<IComposer>(new OwnerId(DigitalBrainNames.DefaultOwner), name);

    private static NeuronId BoardId(string name)
        => NeuronId.For<IBoard>(new OwnerId(DigitalBrainNames.DefaultOwner), name);

    private async Task<SignalDelivery> LatestIncoming<T>(NeuronId id) where T : Signal
    {
        var matches = (await Query(id).ReadJournal(JournalKind.Incoming, 0)).Delta
            .Where(delivery => delivery.Signal is T)
            .ToArray();
        Assert.True(matches.Length > 0, $"No {typeof(T).Name} in journal for {id}.");
        return matches[^1];
    }

    private async Task<IReadOnlyList<string>> IncomingNotices(NeuronId id)
        => [.. (await Query(id).ReadJournal(JournalKind.Incoming, 0)).Delta
            .Select(delivery => delivery.Signal)
            .OfType<Notice>()
            .Select(notice => notice.Text)];

    private async Task<DigitalBrain.Abstractions.Synapses.Synapse?> Synapse(
        NeuronId source, NeuronId target, string signalType)
    {
        var matches = (await Query(source).ReadSynapses())
            .Where(synapse =>
                synapse.Target == target
                && string.Equals(synapse.SignalType, signalType, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 0 ? null : matches[0];
    }
}
