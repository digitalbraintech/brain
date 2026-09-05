using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Orleans;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class ChatPublicationTests(SimulationFixture fixture)
{
    [Fact]
    public async Task StableBehaviorNoteIsDeduplicatedAfterTranscriptRetentionAndFencesLateOutputs()
    {
        var token = TestContext.Current.CancellationToken;
        var chat = fixture.Sim.Brain.Get<IChat>(fixture.Sim.UniqueId("durable-note"));
        var target = fixture.Sim.Grains.GetGrain<INeuronGrain>(chat.Id.ToGrainId());
        var source = NeuronId.For<IBehavior>(fixture.Sim.Brain.Owner, fixture.Sim.UniqueId("source"));
        var output = SignalDelivery.Create(new Note("Saved behavior result"), source, 1, TimeProvider.System, sourceEpoch: 1);
        await target.Deliver(output, token);
        await target.Deliver(output, token);
        Assert.Single((await chat.RequestAsync(new ReadTranscriptRequest(chat.Id.Name), token)).Transcript.Turns,
            turn => turn.Text == "Saved behavior result");
        for (var index = 0; index < 65; index++)
        {
            await chat.SendAsync(new Note($"Later {index}"), token);
        }
        await target.Deliver(output, token);
        Assert.DoesNotContain((await chat.RequestAsync(new ReadTranscriptRequest(chat.Id.Name), token)).Transcript.Turns,
            turn => turn.Text == "Saved behavior result");

        await target.FenceSourceEpoch(source, 2);
        await target.Deliver(SignalDelivery.Create(new Note("Late disabled output"), source, 2, TimeProvider.System, sourceEpoch: 1), token);
        await target.FenceSourceStream(source, "pr:1", 3);
        await target.Deliver(SignalDelivery.Create(new Note("Old PR head"), source, 3, TimeProvider.System,
            sourceEpoch: 2, sourceStream: "pr:1", streamGeneration: 2), token);
        await target.Deliver(SignalDelivery.Create(new Note("Unrelated PR"), source, 4, TimeProvider.System,
            sourceEpoch: 2, sourceStream: "pr:2", streamGeneration: 1), token);
        var turns = (await chat.RequestAsync(new ReadTranscriptRequest(chat.Id.Name), token)).Transcript.Turns;
        Assert.DoesNotContain(turns, turn => turn.Text is "Late disabled output" or "Old PR head");
        Assert.Single(turns, turn => turn.Text == "Unrelated PR");
    }

    [Fact]
    public async Task ApplicationPublicationIsIdempotentEvenAfterItsTranscriptEntryIsTrimmed()
    {
        var name = fixture.Sim.UniqueId("publication");
        var chat = fixture.Sim.Brain.Get<IChat>(name);
        var token = TestContext.Current.CancellationToken;
        var publication = new PublishNote(Guid.NewGuid(), "Architecture and quality review completed.");
        Assert.False((await chat.RequestAsync(publication, token)).Duplicate);
        Assert.True((await chat.RequestAsync(publication, token)).Duplicate);
        var transcript = await chat.RequestAsync(new ReadTranscriptRequest(name), token);
        Assert.Single(transcript.Transcript.Turns, turn => turn.Text == publication.Text);

        for (var index = 0; index < 65; index++)
        {
            await chat.SendAsync(new Note($"Later note {index}"), token);
        }

        Assert.True((await chat.RequestAsync(publication, token)).Duplicate);
        transcript = await chat.RequestAsync(new ReadTranscriptRequest(name), token);
        Assert.DoesNotContain(transcript.Transcript.Turns, turn => turn.Text == publication.Text);
        await Assert.ThrowsAnyAsync<Exception>(() => chat.RequestAsync(publication with { Text = "Different review" }, token));
    }
}
