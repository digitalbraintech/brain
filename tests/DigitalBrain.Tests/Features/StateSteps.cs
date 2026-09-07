using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class StateSteps(BrainSteps brain)
{
    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at ""(.*)"" (\d+) times")]
    public async Task FireMany(string from, string type, string body, string to, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await brain.FireCore(from, type, body, to);
        }
    }

    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at profile ""(.*)""")]
    public async Task FireAtProfile(string from, string type, string body, string profile)
        => _ = await brain.Neuron(from).Fire(Signal.Create(type, body), ProfileId(profile), null);

    [When(@"""(.*)"" incoming journal is read (\d+) times")]
    public async Task ReadMany(string name, int times)
    {
        for (var i = 0; i < times; i++)
        {
            _ = await brain.Journal(name, JournalKind.Incoming);
        }
    }

    [Then(@"""(.*)"" (incoming|outgoing) tally for ""(\w+)"" is (\d+)")]
    public async Task ThenTally(string name, string kind, string type, long count)
    {
        var snapshot = await Snapshot(brain.Query(name), BrainSteps.Kind(kind));
        Assert.Equal(count, snapshot.Tallies.SingleOrDefault(t => t.SignalType == type)?.Recorded ?? 0);
    }

    [Then(@"""(.*)"" incoming last sequence is (\d+)")]
    public async Task ThenLastSequence(string name, long sequence)
        => Assert.Equal(sequence, (await Snapshot(brain.Query(name), JournalKind.Incoming)).LastSequence);

    [Then(@"""(.*)"" latest ""(\w+)"" is (\{.*\})$")]
    public async Task ThenLatest(string name, string type, string body)
        => Assert.Equal(body, (await brain.Query(name).ReadState()).Single(d => d.Signal.Type == type).Signal.Body);

    [Then(@"""(.*)"" state has (\d+) entries")]
    public async Task ThenStateCount(string name, int count) => Assert.Equal(count, (await brain.Query(name).ReadState()).Count);

    [Then(@"profile ""(.*)"" bio is ""(.*)""")]
    public async Task ThenBio(string profile, string bio)
        => Assert.Equal(bio, await brain.Brain.Grains.GetGrain<IProfile>(ProfileId(profile).ToGrainId()).ReadBio());

    [Then(@"profile ""(.*)"" incoming journal contains ""(\w+)"" (\{.*\})$")]
    public async Task ThenProfileIncoming(string profile, string type, string body)
        => Assert.Contains((await ProfileQuery(profile).ReadJournal(JournalKind.Incoming, 0)).Delta, d => d.Signal.Type == type && d.Signal.Body == body);

    [Then(@"profile ""(.*)"" outgoing journal has (\d+) entries")]
    public async Task ThenProfileOutgoing(string profile, int count)
        => Assert.Equal(count, (await ProfileQuery(profile).ReadJournal(JournalKind.Outgoing, 0)).Delta.Count);

    private static NeuronId ProfileId(string name) => new("profile", name);
    private INeuronQuery ProfileQuery(string name) => brain.Brain.Grains.GetGrain<INeuronQuery>(ProfileId(name).ToGrainId());

    // Read past the tip to get the snapshot without a delta.
    private static async Task<JournalSnapshot> Snapshot(INeuronQuery query, JournalKind kind)
    {
        var tip = await query.ReadJournal(kind, 0);
        var reset = await query.ReadJournal(kind, tip.ResumeSequence + 1);
        return reset.ResetSnapshot ?? throw new InvalidOperationException("Expected a snapshot past the tip.");
    }
}
