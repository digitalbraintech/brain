using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.UI;

[GrainType("chat")]
internal sealed class Chat : Neuron, IChat, IChatKernel
{
    private const string CommandLogName = "chat.command-log";
    private const string TranscriptName = "chat.transcript";
    private const string TurnLogName = "chat.turn-log";
    private const string QueueStateName = "chat.turn-queue";
    private const string ExecutionFocusName = "chat.execution-focus";
    private const string ConversationIdName = "chat.conversation-id";
    private const int RememberedCommands = 64;
    private const int RetainedTurns = 64;
    private const int RetainedTurnRecords = 64;
    private const int MaxRelatedExecutions = 8;
    private const int MaxPublications = 10000;

    // One turn is one awaited worker call; the budget mirrors the kernel SSE edge and the
    // call's own ResponseTimeout. The deadline timer is the belt for a call that never
    // resumes; the grace keeps the two belts from racing on an honest slow finish.
    private static readonly TimeSpan TurnBudget =
        TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture);
    private static readonly TimeSpan TurnDeadlineGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TurnDeadlineCheckPeriod = TimeSpan.FromSeconds(15);

    private readonly IDurableList<byte[]> _commandLog;
    private readonly IDurableList<byte[]> _transcript;
    private readonly IDurableList<byte[]> _turnLog;
    private readonly IDurableValue<byte[]> _queueState;
    private readonly IDurableValue<byte[]> _executionFocus;
    private readonly IDurableValue<Guid> _conversationId;
    private readonly Serializer<OwnerCommand> _commands;
    private readonly Serializer<ChatTurn> _turns;
    private readonly Serializer<DurableTurnRecord> _turnRecords;
    private readonly Serializer<TurnQueueState> _queues;
    private readonly Serializer<ChatExecutionFocus> _focusStates;
    private readonly IDurableList<byte[]> _publicationLog;
    private readonly Serializer<ChatPublication> _publications;

    // The in-flight worker call is detached and tracked: the task settles the turn when the call
    // returns or throws; the token is the turn-scoped cancel; the timer fails a call that
    // outlives its budget. All in-memory — a restarted activation reconciles durably instead.
    private Task? _activeCall;
    private Guid? _activeCallTurnId;
    private CancellationTokenSource? _activeCallCancellation;
    private DateTimeOffset? _activeCallStartedAt;
    private IGrainTimer? _turnDeadlineTimer;

    public Chat(NeuronRuntime runtime)
        : base(runtime)
    {
        _commandLog = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(CommandLogName);
        _transcript = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(TranscriptName);
        _turnLog = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(TurnLogName);
        _queueState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(QueueStateName);
        _executionFocus = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(ExecutionFocusName);
        _conversationId = ServiceProvider.GetRequiredKeyedService<IDurableValue<Guid>>(ConversationIdName);
        _commands = ServiceProvider.GetRequiredService<Serializer<OwnerCommand>>();
        _turns = ServiceProvider.GetRequiredService<Serializer<ChatTurn>>();
        _turnRecords = ServiceProvider.GetRequiredService<Serializer<DurableTurnRecord>>();
        _queues = ServiceProvider.GetRequiredService<Serializer<TurnQueueState>>();
        _focusStates = ServiceProvider.GetRequiredService<Serializer<ChatExecutionFocus>>();
        _publicationLog = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>("chat.publications");
        _publications = ServiceProvider.GetRequiredService<Serializer<ChatPublication>>();
    }

    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await FailUnavailableUserActionsAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await FailTurnInterruptedByRestartAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(SendMessage signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var accepted = await EnqueueTurnAsync(signal)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReplyAsync(accepted).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(CancelTurn signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CancelTurnAsync(signal);
    }

    private async Task CancelTurnAsync(CancelTurn command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireActor(command.Actor, "cancel-turn");

        if (command.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(command));
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == command.TurnId.Value);
        if (index < 0)
        {
            // Idempotent: unknown turn is a settled no-op for duplicate cancels after retention.
            return;
        }

        var record = turns[index];
        if (IsTerminal(record.Status))
        {
            return;
        }

        if (record.Status == ChatTurnStatus.Cancelling)
        {
            // Already flagged; the in-flight call's cancellation settles the turn.
            return;
        }

        var cancellationContext = new AgentTurnContext(Id, new CommandId(record.CommandId), record.Actor, record.AllowedToolNames);
        foreach (var source in ServiceProvider.GetServices<IUserActionSource>())
        {
            source.Cancel(cancellationContext);
        }

        if (record.Status is ChatTurnStatus.Pending or ChatTurnStatus.WaitingForUser)
        {
            turns[index] = record with
            {
                Status = ChatTurnStatus.Cancelled,
                Revision = record.Revision + 1,
                UserAction = null,
                Detail = "cancelled",
            };
            SaveTurns(turns);
            var queue = LoadQueue();
            queue.PendingTurnIds.Remove(record.TurnId);
            SaveQueue(queue);
            await RecordConversationAsync(new TurnLifecycle(
                new TurnId(record.TurnId),
                new CommandId(record.CommandId),
                Id,
                ChatTurnStatus.Cancelled,
                "queued-cancel")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Running head with no tracked call can only mean the tracking activation died and a
        // reconcile has not settled it yet — settle here rather than cancelling nothing.
        if (_activeCallTurnId != record.TurnId)
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Cancelled, result: null, "cancelled")
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        // Running: flag first so Cancelling always precedes Cancelled in the journal, then
        // trip the call's token; the call continuation settles the turn as Cancelled.
        var cancellation = _activeCallCancellation;
        turns[index] = record with { Status = ChatTurnStatus.Cancelling, Revision = record.Revision + 1 };
        SaveTurns(turns);
        await RecordConversationAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            ChatTurnStatus.Cancelling,
            "running-cancel")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellation?.Cancel();
    }

    public Task<ChatTranscript> LoadTranscript() => Task.FromResult(new ChatTranscript(Turns()));

    public async Task<IReadOnlyList<ChatTurnSnapshot>> LoadTurnSnapshots()
    {
        await FailUnavailableUserActionsAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return [.. LoadTurns().Select(static turn => new ChatTurnSnapshot(
                new TurnId(turn.TurnId),
                new CommandId(turn.CommandId),
                turn.Text,
                turn.Status,
                turn.UserAction,
                turn.Answer,
                turn.Detail))];
    }

    public Task HandleAsync(CompleteUserAction signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return CompleteUserActionAsync(signal.Context, signal.ActionId, signal.Accepted);
    }

    private async Task CompleteUserActionAsync(AgentTurnContext context, string actionId, bool accepted)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.CommandId == context.CommandId.Value);
        if (context.Chat != Id || index < 0)
        {
            return;
        }

        var record = turns[index];
        if (record.Status != ChatTurnStatus.WaitingForUser
            || record.Actor != context.Actor
            || record.UserAction is not { } action
            || !string.Equals(action.Id, actionId, StringComparison.Ordinal))
        {
            // Cancellation and an already-consumed callback both win permanently.
            return;
        }

        if (!accepted || action.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Failed, null,
                action.Stage == "readiness"
                    ? "Repository access is saved, but webhook verification timed out. Your behavior draft is retained. Verify the public webhook endpoint, then ask Ino to continue this saved behavior."
                    : "Login was not completed or expired. Please send the request again.")
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (action.ResumeToolNames.Length == 0
            || (action.Provider is "gmail" or "salesforce" && action.SpecialistContinuation is null))
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Completed,
                new ChatTurnResult(
                    "Login completed. Please repeat your request and explicitly confirm any changes you want to make.",
                    "assistant"), detail: null)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        SpecialistContinuation? specialist = null;
        if (action.SpecialistContinuation is { } requested)
        {
            specialist = ServiceProvider.GetServices<IUserActionSource>()
                .Select(source => source.ResolveSpecialistContinuation(context, action.Id))
                .FirstOrDefault(candidate => candidate is not null && candidate.Target == requested.Target
                    && candidate.RequestText == requested.RequestText
                    && candidate.AllowedToolNames.SequenceEqual(requested.AllowedToolNames, StringComparer.Ordinal));
            if (specialist is null)
            {
                await SettleTurnAsync(record.TurnId, ChatTurnStatus.Failed, null,
                    "Login completed, but its specialist connection is no longer available. Please send the request again.")
                    .ConfigureAwait(true);
                return;
            }
        }

        // Trust only stored provider control data, never callback or user input.
        turns[index] = record with
        {
            Status = ChatTurnStatus.Pending,
            Revision = record.Revision + 1,
            UserAction = null,
            AllowedToolNames = [.. action.ResumeToolNames],
            CompletedUserActionId = action.Id,
            SpecialistContinuation = specialist,
            SetupContinuation = action.SetupContinuation,
            Answer = null,
            Detail = null,
        };
        SaveTurns(turns);
        var queue = LoadQueue();
        if (!queue.PendingTurnIds.Contains(record.TurnId))
        {
            queue.PendingTurnIds.Add(record.TurnId);
        }

        SaveQueue(queue);
        await RecordConversationAsync(new TurnLifecycle(new TurnId(record.TurnId), context.CommandId, Id,
            ChatTurnStatus.Pending, "login-completed"))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task FailUnavailableUserActionsAsync()
    {
        var now = TimeProvider.GetUtcNow();
        var sources = ServiceProvider.GetServices<IUserActionSource>().ToArray();
        foreach (var record in LoadTurns().Where(turn => turn.Status == ChatTurnStatus.WaitingForUser))
        {
            var action = record.UserAction;
            var available = action is null ? null : FindAvailableUserAction(record, sources);
            if (action is not null && action.ExpiresAt > now
                && available?.Id == action.Id)
            {
                if (available.Message != action.Message || available.Stage != action.Stage)
                {
                    var turns = LoadTurns();
                    var index = turns.FindIndex(turn => turn.TurnId == record.TurnId);
                    turns[index] = record with { UserAction = available, Revision = record.Revision + 1 };
                    SaveTurns(turns);
                    await RecordConversationAsync(new Responded(new CommandId(record.CommandId), Id,
                        available.Message, Author: "assistant", UserAction: available, TurnId: new TurnId(record.TurnId))).ConfigureAwait(true);
                    await WriteStateAsync().ConfigureAwait(true);
                }
                continue;
            }

            if (action is not null)
            {
                var context = new AgentTurnContext(Id, new CommandId(record.CommandId), record.Actor, record.AllowedToolNames);
                using var actor = VerifiedActor.Enter(record.Actor);
                var replacement = sources.Select(source => source.Recover(context, action)).FirstOrDefault(candidate => candidate is not null);
                if (replacement is not null)
                {
                    var turns = LoadTurns();
                    var index = turns.FindIndex(turn => turn.TurnId == record.TurnId);
                    turns[index] = record with { UserAction = replacement, Revision = record.Revision + 1 };
                    SaveTurns(turns);
                    await RecordConversationAsync(new Responded(new CommandId(record.CommandId), Id,
                        replacement.Message, Author: "assistant", UserAction: replacement, TurnId: new TurnId(record.TurnId)))
                        .ConfigureAwait(true);
                    await WriteStateAsync().ConfigureAwait(true);
                    continue;
                }
            }

            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Failed, null,
                action?.Stage == "readiness"
                    ? "GitHub access is saved, but webhook verification timed out. Your behavior draft is retained. Verify the public webhook endpoint, then ask Ino to continue this saved behavior."
                    : "Login expired or was interrupted by a restart. Please send the request again.")
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private UserActionRequest? FindAvailableUserAction(
        DurableTurnRecord record, IEnumerable<IUserActionSource> sources)
    {
        var command = new CommandId(record.CommandId);
        using var scope = AgentTurnContext.Enter(new AgentTurnContext(Id, command, record.Actor, record.AllowedToolNames));
        return sources.Select(source => source.Find(Id.Owner, command)).FirstOrDefault(action => action?.Id == record.UserAction?.Id);
    }

    public Task HandleAsync(SetActiveExecution signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        SaveActiveExecution(signal.ExecutionId);
        return Task.CompletedTask;
    }

    public Task<ExecutionId?> LoadActiveExecution()
        => Task.FromResult(LoadFocus().ActiveExecutionId);

    private void SaveActiveExecution(ExecutionId? id)
    {
        var focus = LoadFocus();
        var related = new List<ExecutionId>(focus.RelatedExecutionIds);
        if (focus.ActiveExecutionId is { } previous && previous != id)
        {
            related.RemoveAll(existing => existing == previous);
            related.Insert(0, previous);
            while (related.Count > MaxRelatedExecutions)
            {
                related.RemoveAt(related.Count - 1);
            }
        }

        if (id is { } active)
        {
            related.RemoveAll(existing => existing == active);
        }

        SaveFocus(new ChatExecutionFocus(id, related));
    }

    public async Task HandleAsync(ReadTurns signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var turns = await LoadTurnSnapshots().WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReplyAsync(new TurnsRead(turns)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ReadActiveExecution signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(new ActiveExecution(LoadFocus().ActiveExecutionId))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ReadTranscriptRequest signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);

        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(
            new TranscriptRead(signal.CommandId, Id, Trimmed(new ChatTranscript(Turns()), signal.MaxTurns)))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(PublishNote signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (signal.PublicationId == Guid.Empty || string.IsNullOrWhiteSpace(signal.Text) || signal.Text.Length > 100_000)
        {
            throw new ArgumentException("A publication requires an identity and between 1 and 100000 characters.", nameof(signal));
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signal.Text)));
        var existing = _publicationLog.Select(RequirePublication).FirstOrDefault(entry => entry.Id == signal.PublicationId);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A publication identity cannot be reused for different content.");
            }

            await ReplyAsync(new NotePublished(signal.PublicationId, Duplicate: true)).ConfigureAwait(true);
            return;
        }

        // Keep compact tombstones even after transcript retention. At capacity fail closed;
        // silently evicting them could duplicate a delayed workflow publication.
        if (_publicationLog.Count >= MaxPublications)
        {
            throw new InvalidOperationException("This conversation has reached its application publication capacity.");
        }

        Remember(new ChatTurn(FromUser: false, signal.Text));
        _publicationLog.Add(_publications.SerializeToArray(new ChatPublication(signal.PublicationId, hash)));
        await RecordConversationAsync(new Responded(new CommandId(signal.PublicationId), Id, signal.Text, Author: Id.Name)).ConfigureAwait(true);
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);
        await ReplyAsync(new NotePublished(signal.PublicationId, Duplicate: false)).ConfigureAwait(true);
    }

    public async Task HandleAsync(Note signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(signal.Text))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses an empty note.");
        }

        var publicationId = CurrentDelivery?.SignalId.Value ?? Guid.NewGuid();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signal.Text)));
        var existing = _publicationLog.Select(RequirePublication).FirstOrDefault(entry => entry.Id == publicationId);
        if (existing is not null)
        {
            if (existing.ContentHash != hash)
            {
                throw new InvalidOperationException("A note delivery identity cannot be reused for different content.");
            }
            return;
        }
        if (_publicationLog.Count >= MaxPublications)
        {
            throw new InvalidOperationException("This conversation has reached its application publication capacity.");
        }
        Remember(new ChatTurn(FromUser: false, signal.Text));
        _publicationLog.Add(_publications.SerializeToArray(new ChatPublication(publicationId, hash)));
        await RecordConversationAsync(new Responded(new CommandId(publicationId), Id, signal.Text, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(KitCardOffer signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(signal.Kind)
            || string.IsNullOrWhiteSpace(signal.Name)
            || string.IsNullOrWhiteSpace(signal.Caption))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses an incomplete kit card.");
        }

        Remember(new ChatTurn(FromUser: false, signal.Caption));
        await RecordConversationAsync(new Responded(CommandId.New(), Id, signal.Caption, Author: Id.Name, Cards: [signal]))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // A fresh activation cannot resume an in-flight worker call (the awaiting task died with
    // the previous activation), so a durably Running head is settled as Failed.
    private async Task FailTurnInterruptedByRestartAsync()
    {
        var queue = LoadQueue();
        if (queue.ActiveTurnId is not { } activeTurnId)
        {
            await TryStartNextAsync().ConfigureAwait(true);
            return;
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == activeTurnId);
        if (index < 0 || IsTerminal(turns[index].Status))
        {
            SaveQueue(queue with { ActiveTurnId = null });
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var interrupted = turns[index];
        if (CanRecoverSetupTurn(interrupted))
        {
            turns[index] = interrupted with
            {
                Status = ChatTurnStatus.Pending,
                Revision = interrupted.Revision + 1,
                SetupRecoveryAttempts = interrupted.SetupRecoveryAttempts + 1,
                Detail = "Resuming the saved connection setup after restart.",
            };
            SaveTurns(turns);
            if (!queue.PendingTurnIds.Contains(activeTurnId))
            {
                queue.PendingTurnIds.Insert(0, activeTurnId);
            }
            SaveQueue(queue with { ActiveTurnId = null });
            await RecordConversationAsync(new TurnLifecycle(new TurnId(activeTurnId), new CommandId(interrupted.CommandId), Id,
                ChatTurnStatus.Pending, "setup-recovered")).ConfigureAwait(true);
            await WriteStateAsync().ConfigureAwait(true);
            await TryStartNextAsync().ConfigureAwait(true);
            return;
        }

        await SettleTurnAsync(activeTurnId, ChatTurnStatus.Failed, result: null, "turn-interrupted")
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    internal static bool CanRecoverSetupTurn(DurableTurnRecord turn)
        => turn.Status == ChatTurnStatus.Running && turn.SetupRecoveryAttempts < 3
            && turn.CompletedUserActionId is not null
            && turn.SetupContinuation is { ToolName: "connect_github_repository" }
            && turn.AllowedToolNames is ["connect_github_repository"];

    private async Task<TurnAccepted> EnqueueTurnAsync(SendMessage message)
    {
        RequireActor(message.Actor, "send");
        await FailUnavailableUserActionsAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (!IsUnseenCommand(message))
        {
            var existing = LoadTurns().FirstOrDefault(turn => turn.CommandId == message.CommandId.Value);
            if (existing is not null)
            {
                return new TurnAccepted(
                    new TurnId(existing.TurnId),
                    message.CommandId,
                    existing.Status);
            }

            throw new NeuronAuthorizationException(
                $"Chat '{Id}' already saw command '{message.CommandId}' without a retained turn record.");
        }

        await RememberOwnerTurnAsync(message).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var turnId = Guid.NewGuid();
        var record = new DurableTurnRecord(
            turnId,
            message.CommandId.Value,
            message.Text,
            message.Actor!,
            ChatTurnStatus.Pending,
            Revision: 0);
        var turns = LoadTurns();
        turns.Add(record);
        TrimTurns(turns);
        SaveTurns(turns);

        var queue = LoadQueue();
        queue.PendingTurnIds.Add(turnId);
        SaveQueue(queue);

        await RecordConversationAsync(new TurnLifecycle(
            new TurnId(turnId),
            message.CommandId,
            Id,
            ChatTurnStatus.Pending)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var started = LoadTurns().First(turn => turn.TurnId == turnId);
        return new TurnAccepted(new TurnId(turnId), message.CommandId, started.Status);
    }

    private async Task TryStartNextAsync()
    {
        var queue = LoadQueue();
        if (queue.ActiveTurnId is not null)
        {
            return;
        }

        if (queue.PendingTurnIds.Count == 0)
        {
            return;
        }

        var nextTurnId = queue.PendingTurnIds[0];
        queue.PendingTurnIds.RemoveAt(0);

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == nextTurnId);
        if (index < 0)
        {
            SaveQueue(queue);
            return;
        }

        var record = turns[index];
        if (record.Status != ChatTurnStatus.Pending)
        {
            SaveQueue(queue);
            await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var running = record with { Status = ChatTurnStatus.Running, Revision = record.Revision + 1 };
        turns[index] = running;
        SaveTurns(turns);
        SaveQueue(queue with { ActiveTurnId = record.TurnId });
        // Stay activated for the whole attempt so idle deactivation cannot orphan a live head.
        DelayDeactivation(TurnBudget + TurnDeadlineGrace + TurnDeadlineCheckPeriod);

        // Running is committed to the journal BEFORE the call starts, so a instantly-settling
        // worker can never put Responded/Completed ahead of Running.
        await RecordConversationAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            ChatTurnStatus.Running)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        StartTurnCall(running);
    }

    private void StartTurnCall(DurableTurnRecord record)
    {
        _activeCallCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _activeCallCancellation = cancellation;
        _activeCallTurnId = record.TurnId;
        _activeCallStartedAt = TimeProvider.GetUtcNow();
        _activeCall = RunTurnAsync(record, cancellation.Token);
        _turnDeadlineTimer ??= this.RegisterGrainTimer(
            FailOverBudgetTurnAsync,
            dueTime: TurnDeadlineCheckPeriod,
            period: TurnDeadlineCheckPeriod);
    }

    private async Task RunTurnAsync(DurableTurnRecord record, CancellationToken cancellationToken)
    {
        try
        {
            var worker = GrainFactory.GetGrain<IChatTurnWorker>(ChatTurnWorker.ForChat(Id).ToGrainId());
            var goal = new ChatTurnGoal(
                record.TurnId,
                new CommandId(record.CommandId),
                record.Text,
                record.Actor,
                Id,
                record.AllowedToolNames,
                record.CompletedUserActionId,
                record.SpecialistContinuation,
                record.SetupContinuation,
                ConversationCorrelation);
            var result = await worker.RunAsync(goal, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await SettleTurnAsync(record.TurnId,
                result.UserAction is null ? ChatTurnStatus.Completed : ChatTurnStatus.WaitingForUser,
                result, detail: null)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Cancelled, result: null, "cancelled")
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            await SettleTurnAsync(record.TurnId, ChatTurnStatus.Failed, result: null, failure.Message)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private async Task FailOverBudgetTurnAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_activeCallTurnId is not { } turnId || _activeCallStartedAt is not { } startedAt)
        {
            _turnDeadlineTimer?.Dispose();
            _turnDeadlineTimer = null;
            return;
        }

        if (TimeProvider.GetUtcNow() - startedAt < TurnBudget + TurnDeadlineGrace)
        {
            return;
        }

        if (_activeCall is { IsCompleted: true })
        {
            // The call already returned; its settle continuation is queued — let it land.
            return;
        }

        // Fail durably FIRST so the call continuation's late cancellation lands on a settled
        // turn, then trip the token to reclaim the worker. The source is detached before the
        // settle so the next turn's start cannot dispose it out from under this callback.
        var cancellation = _activeCallCancellation;
        _activeCallCancellation = null;
        await SettleTurnAsync(
            turnId,
            ChatTurnStatus.Failed,
            result: null,
            $"turn-budget-exceeded after {TurnBudget}").ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    // The single settle point for every turn outcome: worker result, worker failure,
    // cancellation, budget overrun, restart reconcile. Records the frozen journal footprint —
    // Responded (Completed with an answer only) then the terminal TurnLifecycle — and
    // advances the FIFO.
    private async Task SettleTurnAsync(
        Guid turnId,
        ChatTurnStatus status,
        ChatTurnResult? result,
        string? detail)
    {
        if (_activeCallTurnId == turnId)
        {
            _activeCallTurnId = null;
            _activeCallStartedAt = null;
            _activeCall = null;
        }

        var turns = LoadTurns();
        var index = turns.FindIndex(turn => turn.TurnId == turnId);
        if (index < 0 || IsTerminal(turns[index].Status))
        {
            // Already settled (deadline beat the continuation, or retention dropped it) —
            // only make sure the queue is not stuck on it.
            var queueDone = LoadQueue();
            if (queueDone.ActiveTurnId == turnId)
            {
                SaveQueue(queueDone with { ActiveTurnId = null });
                await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            return;
        }

        var record = turns[index];
        if (record.Status == ChatTurnStatus.Cancelling)
        {
            status = ChatTurnStatus.Cancelled;
            result = null;
            detail = "cancelled";
        }

        turns[index] = record with
        {
            Status = status,
            Revision = record.Revision + 1,
            UserAction = status == ChatTurnStatus.WaitingForUser ? result?.UserAction : null,
            Answer = result?.Answer,
            Detail = detail,
        };
        SaveTurns(turns);

        if (status is ChatTurnStatus.Completed or ChatTurnStatus.WaitingForUser)
        {
            await TryRecordRespondedAsync(record, result)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await RecordConversationAsync(new TurnLifecycle(
            new TurnId(record.TurnId),
            new CommandId(record.CommandId),
            Id,
            status,
            detail)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (status == ChatTurnStatus.Completed && result is not null)
        {
            var publishedContext = new AgentTurnContext(Id, new CommandId(record.CommandId), record.Actor, record.AllowedToolNames);
            foreach (var handler in ServiceProvider.GetServices<ITrustedUserCommandHandler>())
            {
                handler.ResponsePublished(publishedContext, result.Answer);
            }
        }

        var queue = LoadQueue();
        if (queue.ActiveTurnId == record.TurnId)
        {
            SaveQueue(queue with { ActiveTurnId = null });
        }

        DelayDeactivation(TimeSpan.FromMinutes(1));
        await TryStartNextAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task TryRecordRespondedAsync(DurableTurnRecord record, ChatTurnResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Answer))
        {
            return;
        }

        await RecordConversationAsync(new Responded(
            new CommandId(record.CommandId),
            Id,
            result.Answer,
            Author: result.Author,
            UserAction: result.UserAction,
            TurnId: new TurnId(record.TurnId)))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var existing = Turns();
        if (!existing.Any(turn => !turn.FromUser
            && string.Equals(turn.Text, result.Answer, StringComparison.Ordinal)))
        {
            Remember(new ChatTurn(FromUser: false, result.Answer));
        }

    }

    private static bool IsTerminal(ChatTurnStatus status)
        => status is ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled;

    private static ChatTranscript Trimmed(ChatTranscript transcript, int? maxTurns)
        => maxTurns is not { } cap || transcript.Turns.Count <= cap
            ? transcript
            : new ChatTranscript([.. transcript.Turns.Skip(transcript.Turns.Count - cap)]);

    private IReadOnlyList<ChatTurn> Turns() => [.. _transcript.Select(RequireTurn)];

    private bool IsUnseenCommand(SendMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);
        if (message.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(message));
        }

        for (var remembered = _commandLog.Count - 1; remembered >= 0; remembered--)
        {
            var command = RequireOwnerCommand(_commands.Deserialize(_commandLog[remembered]));
            if (command.CommandId != message.CommandId.Value)
            {
                continue;
            }

            if (!string.Equals(command.Text, message.Text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A chat command id cannot be reused with different text.");
            }

            return false;
        }

        return true;
    }

    private Task RememberOwnerTurnAsync(SendMessage message)
    {
        Remember(message.CommandId, message.Text, message.Actor);
        Remember(new ChatTurn(FromUser: true, message.Text));
        return Task.CompletedTask;
    }

    private void Remember(CommandId commandId, string text, ActorContext? actor)
        => Append(
            _commandLog,
            _commands.SerializeToArray(new OwnerCommand(commandId.Value, text, actor)),
            RememberedCommands);

    private void Remember(ChatTurn turn)
        => Append(_transcript, _turns.SerializeToArray(turn), RetainedTurns);

    private List<DurableTurnRecord> LoadTurns()
        => [.. _turnLog.Select(RequireTurnRecord)];

    private void SaveTurns(List<DurableTurnRecord> turns)
    {
        while (_turnLog.Count > 0)
        {
            _turnLog.RemoveAt(_turnLog.Count - 1);
        }

        foreach (var turn in turns)
        {
            _turnLog.Add(_turnRecords.SerializeToArray(turn));
        }
    }

    private void TrimTurns(List<DurableTurnRecord> turns)
    {
        while (turns.Count > RetainedTurnRecords)
        {
            turns.RemoveAt(0);
        }
    }

    private TurnQueueState LoadQueue()
    {
        if (_queueState.Value is not { Length: > 0 } bytes)
        {
            return new TurnQueueState([], null);
        }

        return RequireQueue(_queues.Deserialize(bytes));
    }

    private void SaveQueue(TurnQueueState queue)
        => _queueState.Value = _queues.SerializeToArray(queue);

    private ChatExecutionFocus LoadFocus()
    {
        if (_executionFocus.Value is not { Length: > 0 } bytes)
        {
            return new ChatExecutionFocus(null, []);
        }

        return RequireFocus(_focusStates.Deserialize(bytes));
    }

    private void SaveFocus(ChatExecutionFocus focus)
        => _executionFocus.Value = _focusStates.SerializeToArray(focus);

    private CorrelationId ConversationCorrelation
    {
        get
        {
            if (_conversationId.Value == Guid.Empty)
            {
                _conversationId.Value = Guid.NewGuid();
            }

            return new CorrelationId(_conversationId.Value);
        }
    }

    private Task RecordConversationAsync(Signal signal)
        => RecordOutgoingAsync(signal, ConversationCorrelation);

    private static void RequireActor(ActorContext? actor, string command)
    {
        if (actor is null)
        {
            throw new NeuronAuthorizationException(
                $"Chat refuses durable owner command '{command}' without an Actor stamp.");
        }

        if (string.IsNullOrWhiteSpace(actor.Username))
        {
            throw new NeuronAuthorizationException(
                $"Chat refuses durable owner command '{command}' with an empty actor username.");
        }
    }

    private static void Append(IDurableList<byte[]> entries, byte[] entry, int retained)
    {
        entries.Add(entry);
        while (entries.Count > retained)
        {
            entries.RemoveAt(0);
        }
    }

    // Orleans 10.3 marks Serializer<T>.Deserialize as [MaybeNull]; durable bytes here must decode.
    private ChatPublication RequirePublication(byte[] bytes)
        => _publications.Deserialize(bytes)
            ?? throw new InvalidOperationException("ChatPublication deserialize returned null.");

    private ChatTurn RequireTurn(byte[] bytes)
        => _turns.Deserialize(bytes)
            ?? throw new InvalidOperationException("ChatTurn deserialize returned null.");

    private static OwnerCommand RequireOwnerCommand(OwnerCommand? command)
        => command ?? throw new InvalidOperationException("OwnerCommand deserialize returned null.");

    private DurableTurnRecord RequireTurnRecord(byte[] bytes)
        => _turnRecords.Deserialize(bytes)
            ?? throw new InvalidOperationException("DurableTurnRecord deserialize returned null.");

    private static TurnQueueState RequireQueue(TurnQueueState? queue)
        => queue ?? throw new InvalidOperationException("TurnQueueState deserialize returned null.");

    private static ChatExecutionFocus RequireFocus(ChatExecutionFocus? focus)
        => focus ?? throw new InvalidOperationException("ChatExecutionFocus deserialize returned null.");
}
