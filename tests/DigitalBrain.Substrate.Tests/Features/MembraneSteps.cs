using DigitalBrain.Abstractions;
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
public sealed class MembraneSteps
{
    private BrainSimulation? _brain;
    private DeliveryOutcome? _lastOutcome;
    private int _lastBroadcastCount;

    [Given("a running brain")]
    public async Task GivenARunningBrain()
    {
        _brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
    }

    [Given(@"timeline ""(.*)"" can handle NewPost")]
    public async Task GivenTimelineCanHandleNewPost(string name)
        => await Query(TimelineId(name)).ReadJournal(JournalKind.Incoming, 0);

    [Given(@"profile ""(.*)"" exists")]
    public async Task GivenProfileExists(string name)
        => _ = await Profile(name).Read();

    [Given(@"timeline ""(.*)"" subscribes to account ""(.*)"" for NewPost")]
    public Task GivenTimelineSubscribesToAccount(string timeline, string account)
        => Brain.Grains.GetGrain<ITimeline>(TimelineId(timeline).ToGrainId())
            .HandleAsync(new Subscribe(AccountId(account), nameof(NewPost)), CancellationToken.None);

    [Given(@"account ""(.*)"" has introduced NewPost to timeline ""(.*)""")]
    public async Task GivenAccountIntroducedNewPost(string account, string timeline)
    {
        await WhenAccountFiresAtTimeline(account, "introduction", timeline);
        Assert.Equal(DeliveryOutcome.Handled, _lastOutcome);
    }

    [When(@"account ""(.*)"" fires NewPost ""(.*)"" at timeline ""(.*)""")]
    public async Task WhenAccountFiresAtTimeline(string account, string text, string timeline)
        => _lastOutcome = await Account(account).FireNewPostAt("timeline", timeline, text);

    [When(@"account ""(.*)"" fires NewPost ""(.*)"" at account ""(.*)""")]
    public async Task WhenAccountFiresAtAccount(string source, string text, string target)
        => _lastOutcome = await Account(source).FireNewPostAt("account", target, text);

    [When(@"account ""(.*)"" broadcasts NewPost ""(.*)""")]
    public async Task WhenAccountBroadcastsNewPost(string account, string text)
        => _lastBroadcastCount = await Account(account).BroadcastNewPost(text);

    [When(@"account ""(.*)"" broadcasts Secret ""(.*)""")]
    public async Task WhenAccountBroadcastsSecret(string account, string text)
        => _lastBroadcastCount = await Account(account).BroadcastSecret(text);

    [When(@"profile ""(.*)"" is saved with bio ""(.*)""")]
    public Task WhenProfileIsSaved(string name, string bio)
        => Profile(name).WriteBio(bio);

    [Then(@"the delivery to timeline ""(.*)"" is handled")]
    [Then(@"the delivery to account ""(.*)"" is handled")]
    public void ThenDeliveryIsHandled(string _)
        => Assert.Equal(DeliveryOutcome.Handled, _lastOutcome);

    [Then(@"the delivery to timeline ""(.*)"" is unhandled")]
    [Then(@"the delivery to account ""(.*)"" is unhandled")]
    public void ThenDeliveryIsUnhandled(string _)
        => Assert.Equal(DeliveryOutcome.Unhandled, _lastOutcome);

    [Then(@"the broadcast reaches (\d+) receivers")]
    public void ThenBroadcastReaches(int count)
        => Assert.Equal(count, _lastBroadcastCount);

    [Then(@"the broadcast reaches at least (\d+) receiver")]
    public void ThenBroadcastReachesAtLeast(int count)
        => Assert.True(
            _lastBroadcastCount >= count,
            $"broadcast reached {_lastBroadcastCount}, expected >= {count}");

    [Then(@"timeline ""(.*)"" incoming journal contains NewPost ""(.*)""")]
    public async Task ThenTimelineIncomingContains(string name, string text)
        => Assert.Contains(text, await IncomingNewPosts(TimelineId(name)));

    [Then(@"account ""(.*)"" incoming journal contains NewPost ""(.*)""")]
    public async Task ThenAccountIncomingContains(string name, string text)
        => Assert.Contains(text, await IncomingNewPosts(AccountId(name)));

    [Then(@"timeline ""(.*)"" incoming journal does not contain NewPost ""(.*)""")]
    public async Task ThenTimelineIncomingDoesNotContain(string name, string text)
        => Assert.DoesNotContain(text, await IncomingNewPosts(TimelineId(name)));

    [Then(@"account ""(.*)"" outgoing journal contains NewPost ""(.*)""")]
    public async Task ThenOutgoingContains(string account, string text)
        => Assert.Contains(text, await OutgoingNewPosts(AccountId(account)));

    [Then(@"the journaled NewPost on timeline ""(.*)"" carries caller account ""(.*)""")]
    public async Task ThenCallerIs(string timeline, string account)
        => Assert.Equal(AccountId(account), (await LatestIncomingNewPost(TimelineId(timeline))).Caller);

    [Then("that delivery has a signal id, correlation id, sequence, and timestamp")]
    public async Task ThenEnvelopeFieldsPresent()
    {
        var delivery = await LatestIncomingNewPost(TimelineId("alice"));
        Assert.NotEqual(default, delivery.SignalId);
        Assert.NotEqual(default, delivery.CorrelationId);
        Assert.True(delivery.Sequence > 0);
        Assert.NotEqual(default, delivery.Timestamp);
    }

    [Then(@"the NewPost payload text is ""(.*)""")]
    public async Task ThenPayloadTextIs(string text)
        => Assert.Equal(
            text,
            Assert.IsType<NewPost>((await LatestIncomingNewPost(TimelineId("alice"))).Signal).Text);

    [Then(@"account ""(.*)"" has a learned NewPost synapse to timeline ""(.*)""")]
    public async Task ThenHasLearnedSynapseToTimeline(string account, string timeline)
    {
        var synapse = await NewPostSynapse(AccountId(account), TimelineId(timeline));
        Assert.NotNull(synapse);
        Assert.Equal(SynapseKind.Learned, synapse.Value.Kind);
    }

    [Then(@"account ""(.*)"" has a bound NewPost synapse to timeline ""(.*)""")]
    public async Task ThenHasBoundSynapseToTimeline(string account, string timeline)
    {
        var synapse = await NewPostSynapse(AccountId(account), TimelineId(timeline));
        Assert.NotNull(synapse);
        Assert.Equal(SynapseKind.Bound, synapse.Value.Kind);
    }

    [Then(@"account ""(.*)"" has no NewPost synapse to timeline ""(.*)""")]
    public async Task ThenHasNoSynapseToTimeline(string source, string timeline)
        => Assert.Null(await NewPostSynapse(AccountId(source), TimelineId(timeline)));

    [Then(@"account ""(.*)"" has no NewPost synapse to account ""(.*)""")]
    public async Task ThenHasNoSynapseToAccount(string source, string target)
        => Assert.Null(await NewPostSynapse(AccountId(source), AccountId(target)));

    [Then(@"account ""(.*)"" has no NewPost synapse to itself")]
    public async Task ThenHasNoSynapseToSelf(string account)
        => Assert.Null(await NewPostSynapse(AccountId(account), AccountId(account)));

    [Then(@"account ""(.*)"" has no synapses")]
    public async Task ThenHasNoSynapses(string account)
        => Assert.Empty(await Query(AccountId(account)).ReadSynapses());

    [Then(@"the NewPost synapse from account ""(.*)"" to timeline ""(.*)"" has fire count (\d+)")]
    public async Task ThenFireCount(string account, string timeline, int fireCount)
    {
        var synapse = await NewPostSynapse(AccountId(account), TimelineId(timeline));
        Assert.NotNull(synapse);
        Assert.Equal(fireCount, synapse.Value.FireCount);
    }

    [Then(@"account ""(.*)"" synapse count is (\d+)")]
    public async Task ThenSynapseCount(string account, int count)
        => Assert.Equal(count, (await Query(AccountId(account)).ReadSynapses()).Count);

    [Then(@"account ""(.*)"" outgoing journal count is (\d+)")]
    public async Task ThenOutgoingCount(string account, int count)
        => Assert.Equal(count, (await Query(AccountId(account)).ReadJournal(JournalKind.Outgoing, 0)).Delta.Count);

    [Then(@"reading profile ""(.*)"" returns bio ""(.*)""")]
    public async Task ThenProfileBio(string name, string bio)
    {
        var state = await Profile(name).Read();
        Assert.NotNull(state);
        Assert.Equal(bio, state.Bio);
    }

    [Then(@"profile ""(.*)"" has no traffic journal")]
    public void ThenProfileHasNoJournal(string _)
        => Assert.DoesNotContain(typeof(INeuronQuery), typeof(IProfile).GetInterfaces());

    [AfterScenario]
    public async Task AfterScenario()
    {
        if (_brain is not null)
        {
            await _brain.DisposeAsync();
            _brain = null;
        }
    }

    private BrainSimulation Brain
        => _brain ?? throw new InvalidOperationException("Given a running brain first.");

    private IAccount Account(string name)
        => Brain.Grains.GetGrain<IAccount>(AccountId(name).ToGrainId());

    private IProfile Profile(string name)
        => Brain.Grains.GetGrain<IProfile>(
            EntityId.For<IProfile>(new OwnerId(DigitalBrainNames.DefaultOwner), name).ToGrainId());

    private INeuronQuery Query(NeuronId id)
        => Brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

    private static NeuronId AccountId(string name)
        => NeuronId.For<IAccount>(new OwnerId(DigitalBrainNames.DefaultOwner), name);

    private static NeuronId TimelineId(string name)
        => NeuronId.For<ITimeline>(new OwnerId(DigitalBrainNames.DefaultOwner), name);

    private async Task<IReadOnlyList<string>> IncomingNewPosts(NeuronId id)
        => NewPostTexts(await Query(id).ReadJournal(JournalKind.Incoming, 0));

    private async Task<IReadOnlyList<string>> OutgoingNewPosts(NeuronId id)
        => NewPostTexts(await Query(id).ReadJournal(JournalKind.Outgoing, 0));

    private async Task<SignalDelivery> LatestIncomingNewPost(NeuronId id)
    {
        var posts = (await Query(id).ReadJournal(JournalKind.Incoming, 0)).Delta
            .Where(delivery => delivery.Signal is NewPost)
            .ToArray();
        Assert.True(posts.Length > 0, "No NewPost in journal.");
        return posts[^1];
    }

    private async Task<Synapse?> NewPostSynapse(NeuronId source, NeuronId target)
    {
        var matches = (await Query(source).ReadSynapses())
            .Where(synapse =>
                synapse.Target == target
                && string.Equals(synapse.SignalType, nameof(NewPost), StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 0 ? null : matches[0];
    }

    private static List<string> NewPostTexts(JournalRead read)
        => [.. read.Delta.Select(delivery => delivery.Signal).OfType<NewPost>().Select(post => post.Text)];
}
