using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class GraphTools(IDigitalBrain brain)
{
    private static readonly PrincipalId Operator =
        new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));

    [McpServerTool(Name = McpSurface.ReadSynapses)]
    [Description("Read source-owned synapses on a neuron. Use to verify subscribe/unsubscribe.")]
    public async Task<string> ReadSynapsesAsync(
        [Description("Neuron kind: chat, xaccount, behavior, usermessages")] string kind,
        [Description("Local instance name, for example 'main' or 'probe'")] string name = "default",
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var synapses = await Resolve(kind, name).SynapsesAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(synapses.Select(edge => new
        {
            source = edge.Source.ToString(),
            target = edge.Target.ToString(),
            signalType = edge.SignalType,
            kind = edge.Kind.ToString(),
            fireCount = edge.FireCount,
        }), JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.ReadJournal)]
    [Description("Read a neuron's incoming or outgoing journal. Use to verify publish/broadcast delivery.")]
    public async Task<string> ReadJournalAsync(
        [Description("Neuron kind: chat, xaccount, behavior, usermessages")] string kind,
        [Description("Local instance name")] string name = "default",
        [Description("Incoming or Outgoing")] string direction = "Outgoing",
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        if (!Enum.TryParse<JournalKind>(direction, ignoreCase: true, out var journalKind))
        {
            throw new ArgumentException("direction must be Incoming or Outgoing.", nameof(direction));
        }

        var read = await Resolve(kind, name).JournalAsync(journalKind, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            resumeSequence = read.ResumeSequence,
            truncated = read.ResetSnapshot is not null,
            deliveries = read.Delta.Select(delivery => new
            {
                signalType = delivery.Signal.GetType().Name,
                correlationId = delivery.CorrelationId.ToString(),
                caller = delivery.Caller.ToString(),
                sequence = delivery.Sequence,
                summary = delivery.Signal.ToString(),
            }),
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.Subscribe)]
    [Description("Write a Bound synapse: the subscriber handles Subscribe so the source owns the edge.")]
    public async Task<string> SubscribeAsync(
        [Description("Subscriber kind")] string subscriberKind,
        [Description("Subscriber local name")] string subscriberName,
        [Description("Source kind")] string sourceKind,
        [Description("Source local name")] string sourceName,
        [Description("Signal type name, for example NewPost or UserMessaged")] string signalType,
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var subscriber = Resolve(subscriberKind, subscriberName);
        var source = Resolve(sourceKind, sourceName);
        var outcome = await subscriber.SubscribeAsync(new Subscribe(source.Id, signalType), cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            subscriber = subscriber.Id.ToString(),
            source = source.Id.ToString(),
            signalType,
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.Unsubscribe)]
    [Description("Remove the Bound synapse from the source.")]
    public async Task<string> UnsubscribeAsync(
        [Description("Subscriber kind")] string subscriberKind,
        [Description("Subscriber local name")] string subscriberName,
        [Description("Source kind")] string sourceKind,
        [Description("Source local name")] string sourceName,
        [Description("Signal type name")] string signalType,
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var subscriber = Resolve(subscriberKind, subscriberName);
        var source = Resolve(sourceKind, sourceName);
        var outcome = await subscriber.UnsubscribeAsync(new Unsubscribe(source.Id, signalType), cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            subscriber = subscriber.Id.ToString(),
            source = source.Id.ToString(),
            signalType,
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.PublishPost)]
    [Description("Send PublishPost to an X account neuron. It broadcasts NewPost along its synapses.")]
    public async Task<string> PublishPostAsync(
        [Description("X account local name")] string accountName,
        [Description("Post text")] string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var account = brain.Get<IXAccount>(Instance(accountName));
        var outcome = await account.SendAsync(new PublishPost(text), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            account = account.Id.ToString(),
            text,
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.SendNote)]
    [Description("Send a Note to a chat neuron (point-to-point, not broadcast).")]
    public async Task<string> SendNoteAsync(
        [Description("Chat local name, for example 'main'")] string chatName,
        [Description("Note text")] string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var chat = brain.Get<IChat>(Instance(chatName));
        var outcome = await chat.SendAsync(new Note(text), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            chat = chat.Id.ToString(),
        }, JsonSerializerOptions.Web);
    }

    private IDisposable Enter() => VerifiedActor.Enter(new ActorContext(Operator, "operator"));

    private string Instance(string name) => PrincipalPartition.InstanceName(Operator, name);

    private GraphSubject Resolve(string kind, string name) => kind.Trim().ToLowerInvariant() switch
    {
        "chat" => GraphSubject.Of(brain.Get<IChat>(Instance(name))),
        "xaccount" => GraphSubject.Of(brain.Get<IXAccount>(Instance(name))),
        "behavior" => GraphSubject.Of(brain.Get<IBehavior>(Instance(name))),
        "usermessages" => GraphSubject.Of(brain.Get<IUserMessages>(Instance(name))),
        _ => throw new ArgumentException(
            "kind must be chat, xaccount, behavior, or usermessages.", nameof(kind)),
    };

    private readonly record struct GraphSubject(
        NeuronId Id,
        Func<Subscribe, CancellationToken, Task<DeliveryOutcome>> SubscribeAsync,
        Func<Unsubscribe, CancellationToken, Task<DeliveryOutcome>> UnsubscribeAsync,
        Func<CancellationToken, Task<IReadOnlyList<DigitalBrain.Abstractions.Synapses.Synapse>>> SynapsesAsync,
        Func<JournalKind, CancellationToken, Task<JournalRead>> JournalAsync)
    {
        public static GraphSubject Of<TNeuron>(NeuronReference<TNeuron> neuron)
            where TNeuron : INeuron
            => new(
                neuron.Id,
                (signal, token) => neuron.SendAsync(signal, token),
                (signal, token) => neuron.SendAsync(signal, token),
                neuron.GetSynapsesAsync,
                (kind, token) => neuron.ReadJournalAsync(kind, 0, token));
    }
}
