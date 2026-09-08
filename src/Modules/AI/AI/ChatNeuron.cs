using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI.Contracts;
using DigitalBrain.Core;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.AI;

/// <summary>
/// A group chat: participants (agent neurons), a transcript (this neuron's own incoming
/// journal) and a turn policy (a MAF <see cref="GroupChatManager"/>). <c>Instruct</c> wires the
/// anatomy, <c>Ask</c> starts a run, each <c>Said</c> advances it, and the run's last line is
/// the <c>Reply</c> to whoever asked.
/// </summary>
[GrainType(AIVocabulary.ChatType)]
internal sealed class ChatNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChatState> state,
    IServiceProvider services)
    : Neuron<ChatState>(runtime, state)
{
    internal const string NeedsParticipants = "Instruct this chat with at least two participants before Ask.";

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        try
        {
            switch (delivery.Signal.Type)
            {
                case AIVocabulary.Instruct:
                    await WireAsync(delivery).ConfigureAwait(true);
                    break;
                case AIVocabulary.Ask:
                    await StartAsync(delivery, cancellationToken).ConfigureAwait(true);
                    break;
                case AIVocabulary.Said:
                    await ContinueAsync(delivery, cancellationToken).ConfigureAwait(true);
                    break;
                default:
                    break;
            }
        }
        // A failed reaction leaves the cursor in place and the drain retries it forever, so a
        // broken run answers the asker instead of escaping. Nobody else would ever hear why.
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            services.GetService<ILogger<ChatNeuron>>()?.LogError(failure, "Chat {Neuron} failed to run a turn.", Id);
            await ApologiseAsync(delivery, failure.Message, cancellationToken).ConfigureAwait(true);
        }
    }

    // ---- Instruct: who hears is anatomy ----

    /// <summary>
    /// The chat wires only its own edges. The participant's <c>Said</c> synapse back to the chat
    /// is created by its first <c>Said</c>, because a directed fire creates the synapse it
    /// travels on — so a turn never makes a grain call to another neuron, and never a cycle.
    /// </summary>
    private async Task WireAsync(SignalDelivery delivery)
    {
        var wanted = Participants(Bodies.Instruct(delivery.Signal.Body).Participants);

        foreach (var participant in wanted)
        {
            await Connect(participant, AIVocabulary.Turn).ConfigureAwait(true);
        }

        // Re-instructing a chat is re-wiring it: a participant who is no longer listed stops
        // being invited.
        foreach (var synapse in await ReadSynapses().ConfigureAwait(true))
        {
            if (synapse.SignalType == AIVocabulary.Turn && !wanted.Contains(synapse.Target))
            {
                await Disconnect(synapse.Target, AIVocabulary.Turn).ConfigureAwait(true);
            }
        }
    }

    // ---- Ask: open a run and invite the first speaker ----

    private async Task StartAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var instruct = Bodies.Instruct(await LatestBodyAsync(AIVocabulary.Instruct).ConfigureAwait(true));
        var participants = Participants(instruct.Participants);
        if (participants.Count < 2)
        {
            await FireAsync(
                Signal.Create(AIVocabulary.Reply, Bodies.Write(NeedsParticipants)),
                delivery.Source,
                delivery.CorrelationId,
                cancellationToken).ConfigureAwait(true);
            return;
        }

        var policy = new RunPolicy(
            [.. participants.Select(static p => p.ToString())],
            instruct.Rounds > 0 ? instruct.Rounds : 1,
            Turn: 0);
        var run = new ChatRun(delivery.CorrelationId.ToString(), delivery.Source.ToString(), policy.ToJson(), PendingParticipant: null);
        await InviteAsync(run, policy, delivery, cancellationToken).ConfigureAwait(true);
    }

    // ---- Said: one turn spoken, so either invite the next or answer the asker ----

    private async Task ContinueAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var correlation = delivery.CorrelationId.ToString();
        var run = State?.Runs.FirstOrDefault(r => string.Equals(r.Correlation, correlation, StringComparison.Ordinal));

        // A Said the chat is not waiting for is already the transcript: it is journaled, and
        // that is all a bystander's line ever needs to be.
        if (run is null || !string.Equals(run.PendingParticipant, delivery.Source.ToString(), StringComparison.Ordinal))
        {
            return;
        }

        var policy = RunPolicy.Parse(run.StateJson);
        policy = policy with { Turn = policy.Turn + 1 };
        if (policy.Turn >= policy.TotalTurns)
        {
            await CloseAsync(run, Bodies.Text(delivery.Signal.Body), delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
            return;
        }

        await InviteAsync(run, policy, delivery, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Persists the turn state, then fires <c>Turn</c> at the speaker the manager picked. The
    /// <c>Said</c> that comes back is this neuron's next inbox entry: fire and read, never wait.
    /// </summary>
    private async Task InviteAsync(ChatRun run, RunPolicy policy, SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var next = await SelectAsync(policy, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
        await SaveRunAsync(run with { StateJson = policy.ToJson(), PendingParticipant = next.ToString() }, cancellationToken).ConfigureAwait(true);
        await FireAsync(Signal.Create(AIVocabulary.Turn, "{}"), next, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
    }

    private async Task CloseAsync(ChatRun run, string text, CorrelationId correlation, CancellationToken cancellationToken)
    {
        var asker = NeuronId.TryParse(run.Asker, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"'{run.Asker}' is not a neuron name.");
        await SaveRunAsync(run, cancellationToken, remove: true).ConfigureAwait(true);
        await FireAsync(Signal.Create(AIVocabulary.Reply, Bodies.Write(text)), asker, correlation, cancellationToken).ConfigureAwait(true);
    }

    // ---- the turn policy ----

    /// <summary>
    /// Rebuilds the manager and replays one selection per turn already spoken, so the speaker
    /// for turn N is the one the manager would have picked had it run the whole conversation.
    /// </summary>
    private async Task<NeuronId> SelectAsync(RunPolicy policy, CorrelationId correlation, CancellationToken cancellationToken)
    {
        var agents = policy.Participants.Select(static p => (AIAgent)new Participant(p)).ToArray();
        // No MaximumIterationCount: the manager's own iteration count is advanced by MAF's host,
        // not by selection, so termination is the chat's — it ends the run after TotalTurns.
        var manager = new ReplayableRoundRobin(agents);
        var history = await TranscriptAsync(correlation).ConfigureAwait(true);

        var speaker = agents[0];
        for (var turn = 0; turn <= policy.Turn; turn++)
        {
            speaker = await manager.SelectAsync(history, cancellationToken).ConfigureAwait(true);
        }

        return NeuronId.TryParse(speaker.Id, out var id)
            ? id
            : throw new InvalidOperationException($"'{speaker.Id}' is not a neuron name.");
    }

    /// <summary>
    /// The transcript the manager sees is this chat's own incoming journal for the run's
    /// correlation — the same thing a participant reads before it speaks. Round-robin ignores
    /// it; a manager that picks the next speaker with a model does not.
    /// </summary>
    private async Task<IReadOnlyList<ChatMessage>> TranscriptAsync(CorrelationId correlation)
    {
        // A plain call on this instance, not a grain call: a neuron may read inside its turn.
        var read = await ReadJournal(JournalKind.Incoming, 0).ConfigureAwait(true);
        var history = new List<ChatMessage>();
        foreach (var entry in read.Delta.Where(e => e.CorrelationId == correlation))
        {
            switch (entry.Signal.Type)
            {
                case AIVocabulary.Ask:
                    history.Add(new ChatMessage(ChatRole.User, Bodies.Text(entry.Signal.Body)));
                    break;
                case AIVocabulary.Said:
                    history.Add(new ChatMessage(ChatRole.Assistant, Bodies.Text(entry.Signal.Body)) { AuthorName = entry.Source.ToString() });
                    break;
                default:
                    break;
            }
        }

        return history;
    }

    /// <summary>The manager's speaker selection is protected, so driving it means opening it.</summary>
    private sealed class ReplayableRoundRobin(IReadOnlyList<AIAgent> agents) : RoundRobinGroupChatManager(agents)
    {
        internal ValueTask<AIAgent> SelectAsync(IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
            => SelectNextAgentAsync(history, cancellationToken);
    }

    /// <summary>
    /// A participant as the manager sees it: a name. The agent behind it is a neuron, and it
    /// speaks by firing <c>Said</c> back at this chat, so nothing here is ever run.
    /// </summary>
    private sealed class Participant(string neuron) : AIAgent
    {
        protected override string IdCore => neuron;

        public override string? Name => neuron;

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken)
            => throw NotRun();

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken)
            => throw NotRun();

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken) => throw NotRun();

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedSession, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
            => throw NotRun();

        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
            => throw NotRun();

        private NotSupportedException NotRun()
            => new($"'{neuron}' speaks by firing Said at its chat; its model and its session live in its own neuron.");
    }

    // ---- plumbing ----

    private static List<NeuronId> Participants(IEnumerable<string> names)
        => [.. names.Select(static name => NeuronId.TryParse(name, out var id) ? id : (NeuronId?)null).OfType<NeuronId>()];

    private async Task<string> LatestBodyAsync(string type)
    {
        var latest = await ReadState().ConfigureAwait(true);
        return latest.FirstOrDefault(d => d.Signal.Type == type)?.Signal.Body ?? string.Empty;
    }

    private async Task SaveRunAsync(ChatRun run, CancellationToken cancellationToken, bool remove = false)
    {
        var runs = State is { } current ? new List<ChatRun>(current.Runs) : [];
        runs.RemoveAll(r => string.Equals(r.Correlation, run.Correlation, StringComparison.Ordinal));
        if (!remove)
        {
            runs.Add(run);
        }

        if (runs.Count > ChatState.MaxRuns)
        {
            runs.RemoveRange(0, runs.Count - ChatState.MaxRuns);
        }

        await SaveAsync(new ChatState(runs), cancellationToken).ConfigureAwait(true);
    }

    /// <summary>The asker hears why the chat stopped, because nothing else will tell them.</summary>
    private async Task ApologiseAsync(SignalDelivery delivery, string text, CancellationToken cancellationToken)
    {
        var correlation = delivery.CorrelationId.ToString();
        var run = State?.Runs.FirstOrDefault(r => string.Equals(r.Correlation, correlation, StringComparison.Ordinal));
        NeuronId? asker = run is not null && NeuronId.TryParse(run.Asker, out var known)
            ? known
            : delivery.Signal.Type == AIVocabulary.Ask ? delivery.Source : null;
        if (asker is null)
        {
            return;
        }

        if (run is not null)
        {
            await SaveRunAsync(run, cancellationToken, remove: true).ConfigureAwait(true);
        }

        await FireAsync(Signal.Create(AIVocabulary.Reply, Bodies.Write(text)), asker, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
    }
}
