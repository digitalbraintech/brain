using System.Globalization;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI.Contracts;
using DigitalBrain.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using McpOperations = DigitalBrain.Mcp.BrainOperations;

namespace DigitalBrain.AI;

/// <summary>
/// A Session neuron with a model attached. <c>Instruct</c> is its configuration, held as
/// latest-per-type; <c>Ask</c> is answered with <c>Reply</c> and <c>Turn</c> with <c>Said</c>,
/// both on the incoming correlation. Everything else is journaled and ignored.
/// </summary>
[GrainType(AIVocabulary.AgentType)]
internal sealed class AgentNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AgentState> state,
    IServiceProvider services)
    : Neuron<AgentState>(runtime, state)
{
    // A tool runs on whatever thread the function-invocation loop happens to be on. Grain
    // state may only be touched on the grain's own scheduler, so every tool that fires or
    // connects hops back to the scheduler this turn started on.
    private TaskScheduler? _turnScheduler;

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.Signal.Type is not (AIVocabulary.Ask or AIVocabulary.Turn))
        {
            return;
        }

        var instruct = Bodies.Instruct(await LatestBodyAsync(AIVocabulary.Instruct).ConfigureAwait(true));

        IChatClient client;
        try
        {
            client = Providers.Resolve(services, instruct.Provider);
        }
        catch (SignalRejectedException rejected)
        {
            // Advice, not a crash: a thrown reaction would be retried forever with the same
            // error, and the asker would never hear why.
            await ReplyAsync(delivery, rejected.Message, cancellationToken).ConfigureAwait(true);
            return;
        }

        _turnScheduler = TaskScheduler.Current;
        try
        {
            var tools = new List<AITool>(BrainTools.For(this, new McpOperations(GrainFactory)));
            tools.AddRange(services.GetRequiredService<NativeTools>().Resolve(instruct.Tools));

            var agent = new ChatClientAgent(
                client,
                new ChatClientAgentOptions
                {
                    Name = Id.Name,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instruct.System,
                        Tools = tools,
                        ModelId = instruct.Model,
                    },
                },
                services.GetService<ILoggerFactory>(),
                services);

            var correlation = delivery.CorrelationId.ToString();
            var session = await LoadSessionAsync(agent, correlation, cancellationToken).ConfigureAwait(true);
            var input = delivery.Signal.Type == AIVocabulary.Turn
                ? await TurnContextAsync(delivery).ConfigureAwait(true)
                : Bodies.Text(delivery.Signal.Body);

            var response = await agent.RunAsync(input, session, options: null, cancellationToken).ConfigureAwait(true);
            await SaveSessionAsync(agent, correlation, session, cancellationToken).ConfigureAwait(true);

            var text = response.Text ?? string.Empty;
            var isTurn = delivery.Signal.Type == AIVocabulary.Turn;
            await FireAsync(
                Signal.Create(isTurn ? AIVocabulary.Said : AIVocabulary.Reply, isTurn ? Bodies.Said(Id.ToString(), text) : Bodies.Write(text)),
                delivery.Source,
                delivery.CorrelationId,
                cancellationToken).ConfigureAwait(true);
        }
        // A model that times out cancels with a TaskCanceledException that has nothing to do
        // with this turn's token. Letting it escape would leave the cursor in place and the
        // drain would retry the same timeout forever, so it answers like any other failure.
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            services.GetService<ILogger<AgentNeuron>>()?.LogError(failure, "Agent {Neuron} failed to answer.", Id);
            await ReplyAsync(delivery, failure.Message, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _turnScheduler = null;
        }
    }

    // ---- what the tools call ----

    internal Task<string> FireFromToolAsync(string type, string body, string? to, string? correlation)
        => OnTurnAsync(async () =>
        {
            NeuronId? target = null;
            if (!string.IsNullOrWhiteSpace(to))
            {
                target = NeuronId.TryParse(to, out var parsed)
                    ? parsed
                    : throw new ArgumentException($"'{to}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", nameof(to));
            }

            CorrelationId? tie = null;
            if (!string.IsNullOrWhiteSpace(correlation))
            {
                tie = Guid.TryParse(correlation, out var value)
                    ? new CorrelationId(value)
                    : throw new ArgumentException($"'{correlation}' is not a correlation id.", nameof(correlation));
            }

            // In-process, never a grain call to self: the agent is already inside its own turn.
            var outcome = await FireAsync(Signal.Create(type, body), target, tie).ConfigureAwait(true);
            return JsonSerializer.Serialize(
                new Mcp.FireResult(outcome.SignalId.ToString(), outcome.CorrelationId.ToString(), outcome.Delivered),
                ToolJson);
        });

    // A read is a query, but it still runs on the neuron's own turn and its rejections are
    // advice the model must read, so it takes the same route as the three writes.
    internal Task<string> ReadFromToolAsync(McpOperations reads, string neuron, string? what, long after)
        => OnTurnAsync(async () =>
            // A reaction may read but never wait: the timeout an MCP client may pass is 0 here.
            JsonSerializer.Serialize(await reads.ReadAsync(new Mcp.ReadRequest(neuron, what, after, 0)).ConfigureAwait(true), ToolJson));

    internal Task<string> ConnectFromToolAsync(string from, string to, string type, bool connect)
        => OnTurnAsync(async () =>
        {
            var source = Parse(from, nameof(from));
            var target = Parse(to, nameof(to));

            // A synapse carries a signal type, so the type must be vocabulary before the edge exists.
            _ = Signal.Create(type, "{}");

            if (source == Id)
            {
                await (connect ? Connect(target, type) : Disconnect(target, type)).ConfigureAwait(true);
            }
            else
            {
                var neuron = GrainFactory.GetGrain<INeuron>(source.ToGrainId());
                await (connect ? neuron.Connect(target, type) : neuron.Disconnect(target, type)).ConfigureAwait(true);
            }

            return $"{(connect ? "connected" : "disconnected")} {from} --{type}--> {to}";
        });

    // ---- the turn ----

    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private async Task<string> LatestBodyAsync(string type)
    {
        var latest = await ReadState().ConfigureAwait(true);
        return latest.FirstOrDefault(d => d.Signal.Type == type)?.Signal.Body ?? string.Empty;
    }

    private Task ReplyAsync(SignalDelivery delivery, string text, CancellationToken cancellationToken)
        => FireAsync(Signal.Create(AIVocabulary.Reply, Bodies.Write(text)), delivery.Source, delivery.CorrelationId, cancellationToken);

    // A turn carries no transcript: the participant reads the source's incoming journal for
    // its own correlation and speaks next.
    private async Task<string> TurnContextAsync(SignalDelivery delivery)
    {
        var read = await GrainFactory.GetGrain<INeuron>(delivery.Source.ToGrainId())
            .ReadJournal(JournalKind.Incoming, 0)
            .ConfigureAwait(true);

        var context = new StringBuilder();
        foreach (var entry in read.Delta.Where(e => e.CorrelationId == delivery.CorrelationId))
        {
            if (entry.Signal.Type == AIVocabulary.Ask)
            {
                context.AppendLine(Bodies.Text(entry.Signal.Body));
            }
        }

        foreach (var entry in read.Delta.Where(e => e.CorrelationId == delivery.CorrelationId && e.Signal.Type == AIVocabulary.Said))
        {
            var said = Bodies.SaidLine(entry.Signal.Body);
            if (said.Length > 0)
            {
                context.AppendLine(said);
            }
        }

        context.Append(CultureInfo.InvariantCulture, $"You are {Id}. Speak next.");
        return context.ToString();
    }

    // ---- sessions ----

    private async Task<AgentSession> LoadSessionAsync(AIAgent agent, string correlation, CancellationToken cancellationToken)
    {
        if (State?.Sessions.FirstOrDefault(s => string.Equals(s.Correlation, correlation, StringComparison.Ordinal)) is { } entry)
        {
            using var document = JsonDocument.Parse(entry.SessionJson);
            return await agent.DeserializeSessionAsync(document.RootElement, cancellationToken: cancellationToken).ConfigureAwait(true);
        }

        return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task SaveSessionAsync(AIAgent agent, string correlation, AgentSession session, CancellationToken cancellationToken)
    {
        var json = (await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(true)).GetRawText();
        var sessions = State is { } current ? new List<AgentSessionEntry>(current.Sessions) : [];
        sessions.RemoveAll(s => string.Equals(s.Correlation, correlation, StringComparison.Ordinal));
        sessions.Add(new AgentSessionEntry(correlation, json));
        if (sessions.Count > AgentState.MaxSessions)
        {
            sessions.RemoveRange(0, sessions.Count - AgentState.MaxSessions);
        }

        await SaveAsync(new AgentState(sessions), cancellationToken).ConfigureAwait(true);
    }

    // ---- plumbing ----

    private static NeuronId Parse(string text, string parameter)
        => NeuronId.TryParse(text, out var id)
            ? id
            : throw new ArgumentException($"'{text}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", parameter);

    // A rejection is advice, and the model is the one who must act on it, so a failed tool
    // answers with the kernel's own wording instead of collapsing the turn.
    private async Task<string> OnTurnAsync(Func<Task<string>> work)
    {
        var scheduler = _turnScheduler
            ?? throw new InvalidOperationException("Brain tools may only run during the agent's turn.");
        try
        {
            return await (scheduler == TaskScheduler.Current
                ? work()
                : Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap())
                .ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return $"error: {error.Message}";
        }
    }
}
