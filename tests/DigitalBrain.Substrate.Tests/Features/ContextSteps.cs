using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Testing;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[Binding]
public sealed class ContextSteps(BrainWorld world)
{
    private static readonly OwnerId Owner = new(DigitalBrainNames.DefaultOwner);

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

    [Given(@"(composer|sender|publisher|incomplete|holding) ""(.*)"" can handle Compose")]
    public Task GivenComposeHandler(string role, string name)
        => Query(IdFor(role, name)).ReadJournal(JournalKind.Incoming, 0);

    [Given(@"board ""(.*)"" can handle Notice")]
    public Task GivenBoard(string name)
        => Query(IdFor("board", name)).ReadJournal(JournalKind.Incoming, 0);

    [Given(@"board ""(.*)"" subscribes to (publisher|incomplete) ""(.*)"" for Notice")]
    public Task GivenBoardSubscribes(string board, string sourceRole, string source)
        => world.Brain.Grains.GetGrain<IBoard>(IdFor("board", board).ToGrainId())
            .HandleAsync(new Subscribe(IdFor(sourceRole, source), nameof(Notice)), CancellationToken.None);

    [When(@"(composer|sender|publisher|incomplete|holding) ""(.*)"" is asked to compose ""(.*)""")]
    public Task WhenCompose(string role, string name, string text)
        => AskCompose(role, name, text);

    [When(@"sender ""(.*)"" is asked to ping composer ""(.*)"" with ""(.*)""")]
    public async Task WhenSenderPingsComposer(string sender, string composer, string text)
    {
        await Sender(sender).Aim("composer", composer);
        await AskCompose("sender", sender, text);
    }

    [When(@"echo ""(.*)"" completes ping ""(.*)"" twice with the same pong")]
    public async Task WhenEchoTwice(string echo, string text)
    {
        await Echo(echo).RepeatIdenticalComplete();
        await AskPing(echo, text);
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

    [When(@"the unfinished compose on holding ""(.*)"" is retried")]
    public async Task WhenRetry(string name)
    {
        _error = null;
        _pong = null;
        try
        {
            await Holding(name).RetryUnfinished();
            _pong = await world.Brain.Brain.Get<IHolding>(name)
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

    [Then("the compose fails")]
    public void ThenComposeFails()
        => AssertHonestFailure();

    [Then("the compose fails as incomplete")]
    public void ThenIncomplete()
    {
        AssertHonestFailure();
        Assert.Contains(ExecutionFailureKind.Incomplete, _error!.ToString(), StringComparison.Ordinal);
    }

    [Then("the conflicting complete is rejected")]
    public void ThenConflict()
        => Assert.NotNull(_error);

    [Then(@"the ping to echo ""(.*)"" and the compose on composer ""(.*)"" share a correlation")]
    public async Task ThenSameCorrelation(string echo, string composer)
    {
        var ping = await LatestIncoming<Ping>(IdFor("echo", echo));
        var compose = await LatestIncoming<Compose>(IdFor("composer", composer));
        Assert.Equal(compose.CorrelationId, ping.CorrelationId);
    }

    [Then("those two deliveries have different signal ids")]
    public async Task ThenDifferentSignalIds()
    {
        var ping = await LatestIncoming<Ping>(IdFor("echo", "echo"));
        var compose = await LatestIncoming<Compose>(IdFor("composer", "main"));
        Assert.NotEqual(compose.SignalId, ping.SignalId);
    }

    [Then(@"sender ""(.*)"" has a learned Ping synapse to echo ""(.*)""")]
    public async Task ThenLearnedPing(string sender, string echo)
    {
        var synapse = await Synapse(IdFor("sender", sender), IdFor("echo", echo), nameof(Ping));
        Assert.NotNull(synapse);
        Assert.Equal(SynapseKind.Learned, synapse.Value.Kind);
    }

    [Then(@"sender ""(.*)"" has no Ping synapse to composer ""(.*)""")]
    public async Task ThenNoPingSynapse(string sender, string composer)
        => Assert.Null(await Synapse(IdFor("sender", sender), IdFor("composer", composer), nameof(Ping)));

    [Then(@"publisher ""(.*)"" has a bound Notice synapse to board ""(.*)""")]
    public async Task ThenBoundNotice(string publisher, string board)
    {
        var synapse = await Synapse(IdFor("publisher", publisher), IdFor("board", board), nameof(Notice));
        Assert.NotNull(synapse);
        Assert.Equal(SynapseKind.Bound, synapse.Value.Kind);
    }

    [Then(@"board ""(.*)"" incoming journal contains Notice ""(.*)""")]
    public async Task ThenNotice(string board, string text)
        => Assert.Contains(text, await IncomingNotices(IdFor("board", board)));

    [Then(@"board ""(.*)"" incoming journal does not contain Notice ""(.*)""")]
    public async Task ThenNoNotice(string board, string text)
        => Assert.DoesNotContain(text, await IncomingNotices(IdFor("board", board)));

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
    public async Task AfterScenario()
    {
        if (world.Simulation is not null)
        {
            await world.Simulation.DisposeAsync();
            world.Simulation = null;
        }

        if (_temp is not null && Directory.Exists(_temp))
        {
            try { Directory.Delete(_temp, true); }
            catch (IOException) { }
        }
    }

    private async Task AskCompose(string role, string name, string text)
    {
        _error = null;
        _pong = null;
        try
        {
            _pong = await RequestCompose(role, name, text);
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }

    private async Task AskPing(string echo, string text)
    {
        _error = null;
        _pong = null;
        try
        {
            _pong = await world.Brain.Brain.Get<IEcho>(echo).RequestAsync(new Ping(text));
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }

    private Task<Pong> RequestCompose(string role, string name, string text)
    {
        var brain = world.Brain.Brain;
        var compose = new Compose(text);
        return role switch
        {
            "composer" => brain.Get<IComposer>(name).RequestAsync(compose),
            "sender" => brain.Get<ISender>(name).RequestAsync(compose),
            "publisher" => brain.Get<IPublisher>(name).RequestAsync(compose),
            "incomplete" => brain.Get<IIncomplete>(name).RequestAsync(compose),
            "holding" => brain.Get<IHolding>(name).RequestAsync(compose),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown compose handler."),
        };
    }

    private void AssertHonestFailure()
    {
        Assert.NotNull(_error);
        Assert.DoesNotContain(
            "No execution context is bound.",
            _error.ToString(),
            StringComparison.Ordinal);
    }

    private IEcho Echo(string name)
        => world.Brain.Grains.GetGrain<IEcho>(IdFor("echo", name).ToGrainId());

    private IComposer Composer(string name)
        => world.Brain.Grains.GetGrain<IComposer>(IdFor("composer", name).ToGrainId());

    private ISender Sender(string name)
        => world.Brain.Grains.GetGrain<ISender>(IdFor("sender", name).ToGrainId());

    private IHolding Holding(string name)
        => world.Brain.Grains.GetGrain<IHolding>(IdFor("holding", name).ToGrainId());

    private INeuronQuery Query(NeuronId id)
        => world.Brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

    private static NeuronId IdFor(string role, string name)
        => role switch
        {
            "echo" => NeuronId.For<IEcho>(Owner, name),
            "composer" => NeuronId.For<IComposer>(Owner, name),
            "sender" => NeuronId.For<ISender>(Owner, name),
            "publisher" => NeuronId.For<IPublisher>(Owner, name),
            "incomplete" => NeuronId.For<IIncomplete>(Owner, name),
            "holding" => NeuronId.For<IHolding>(Owner, name),
            "board" => NeuronId.For<IBoard>(Owner, name),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown neuron role."),
        };

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

    private async Task<Synapse?> Synapse(NeuronId source, NeuronId target, string signalType)
    {
        var matches = (await Query(source).ReadSynapses())
            .Where(synapse =>
                synapse.Target == target
                && string.Equals(synapse.SignalType, signalType, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 0 ? null : matches[0];
    }
}
