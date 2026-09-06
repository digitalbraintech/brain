using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Chat;
using DigitalBrain.Product.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

[GrainType(IAgentTurnWorker.GrainTypeName)]
internal sealed class AgentTurnWorker(IGrainFactory grains) : Grain, IAgentTurnWorker
{
    public async Task Enqueue(
        CorrelationId correlation,
        CommandId command,
        string text,
        ActorContext? actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ParseOwner();
        var assistant = new NeuronId("assistant", owner, "assistant");
        var turn = grains.GetGrain<IAssistantTurn>(assistant.ToGrainId());
        var kernel = grains.GetGrain<IAgentKernel>(IAgentKernel.IdFor(owner));
        var transcript = grains.GetGrain<ITranscript>(
            EntityIdFor(owner, actor));

        var actorContext = actor ?? new ActorContext(PrincipalId.New(), "owner");
        await transcript.Append(
                new TranscriptEntry(true, text, command.ToString(), DateTimeOffset.UtcNow),
                ITranscript.DefaultCap)
            .ConfigureAwait(true);

        var history = await transcript.Read().ConfigureAwait(true);
        var messages = (history?.Entries ?? [])
            .Select(entry => new ChatMessage(entry.FromUser ? ChatRole.User : ChatRole.Assistant, entry.Text))
            .ToList();
        if (messages.Count == 0 || messages[^1].Text != text)
        {
            messages.Add(new ChatMessage(ChatRole.User, text));
        }

        var answer = new System.Text.StringBuilder();
        using (VerifiedActor.Enter(actorContext))
        using (AgentTurnContext.Enter(new AgentTurnContext(assistant, command, actorContext)))
        {
            await foreach (var chunk in kernel.AskStreaming(messages, correlation, cancellationToken)
                .ConfigureAwait(true))
            {
                answer.Append(chunk.Text);
            }
        }

        var reply = answer.ToString();
        await transcript.Append(
                new TranscriptEntry(false, reply, command.ToString(), DateTimeOffset.UtcNow),
                ITranscript.DefaultCap)
            .ConfigureAwait(true);
        await turn.RecordTurnFact(new Responded(command, assistant, reply), correlation, cancellationToken)
            .ConfigureAwait(true);
    }

    private OwnerId ParseOwner()
    {
        var key = this.GetPrimaryKeyString();
        var separator = key.IndexOf('/');
        return new OwnerId(separator < 0 ? key : key[..separator]);
    }

    private static GrainId EntityIdFor(OwnerId owner, ActorContext? actor)
    {
        var name = actor is { } context
            ? DigitalBrain.Abstractions.Identity.PrincipalPartition.InstanceName(context.PrincipalId, "main")
            : "main";
        return DigitalBrain.Abstractions.Identity.EntityId.For<ITranscript>(owner, name).ToGrainId();
    }
}
