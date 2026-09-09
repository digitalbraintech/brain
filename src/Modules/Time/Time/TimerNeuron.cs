using DigitalBrain.Product.Identity;
using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Time;

[GrainType("timer")]
public sealed class TimerNeuron : Neuron, ITimer, IRemindable
{
    private const string StateName = "time.timer";
    private const string ReminderPrefix = "time.timer.";
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<TimerState> _states;

    public TimerNeuron(NeuronRuntime runtime)
        : base(runtime)
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<TimerState>>();
    }

    private TimerSnapshot Snapshot()
        => LoadRecorded() is { } data
            ? new TimerSnapshot(data.Status, data.Generation, data.ScheduledAt, data.DueAt, data.Duration, data.Note)
            : new TimerSnapshot(TimerStatus.Unscheduled, Generation: 0, null, null, null, null);

    public async Task HandleAsync(ReadTimer signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(Snapshot())
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(StartTimer signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(signal.CommandId);

        if (signal.DurationSeconds <= 0)
        {
            throw new NeuronAuthorizationException($"Timer '{Id}' refuses a non-positive duration.");
        }

        if (string.IsNullOrWhiteSpace(signal.Note))
        {
            throw new NeuronAuthorizationException($"Timer '{Id}' refuses to arm without a note to deliver.");
        }

        var current = LoadRecorded();
        if (current is { Status: TimerStatus.Scheduled })
        {
            throw new NeuronAuthorizationException(
                $"Timer '{Id}' is already scheduled; cancel it or let it elapse before arming again.");
        }

        var generation = (current?.Generation ?? 0) + 1;
        var scheduledAt = TimeProvider.GetUtcNow();
        var duration = TimeSpan.FromSeconds(signal.DurationSeconds);
        var dueAt = scheduledAt + duration;
        var reminderName = ReminderName(generation);

        Stage(new TimerState(
            TimerStatus.Scheduled,
            generation,
            scheduledAt,
            dueAt,
            duration,
            signal.Note,
            reminderName));

        await RegisterReminderAsync(reminderName, duration).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(
            new TimerScheduled(signal.CommandId, Id, generation, scheduledAt, dueAt, duration, signal.Note))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(CancelTimer signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(signal.CommandId);

        var current = LoadRecorded();
        if (current is not { Status: TimerStatus.Scheduled })
        {
            throw new NeuronAuthorizationException($"Timer '{Id}' has no scheduled timer to cancel.");
        }

        Stage(current with { Status = TimerStatus.Cancelled, ActiveReminderName = null });

        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(
            new TimerCancelled(signal.CommandId, Id, current.Generation))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await RetireReminderAsync(current.ActiveReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!TryParseReminderName(reminderName, out var generation))
        {
            throw new InvalidOperationException(
                $"Timer neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        await ElapseIfDue(generation, reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task ElapseIfDue(long generation, string reminderName)
    {
        var data = LoadRecorded();

        if (data is null
            || data.Status != TimerStatus.Scheduled
            || data.Generation != generation
            || !string.Equals(data.ActiveReminderName, reminderName, StringComparison.Ordinal))
        {
            await RetireReminderAsync(reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var observedAt = TimeProvider.GetUtcNow();
        if (observedAt < data.DueAt)
        {
            await RegisterReminderAsync(reminderName, data.DueAt - observedAt).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var resolution = observedAt > data.DueAt + ReminderPeriod
            ? TimerResolution.Recovered
            : TimerResolution.OnTime;
        var rollbackState = SerializedState();
        Stage(data with { Status = TimerStatus.Elapsed, ActiveReminderName = null });

        try
        {
            await RecordOutgoingAsync(new TimerElapsed(
                Id,
                generation,
                data.ScheduledAt,
                data.DueAt,
                observedAt,
                resolution,
                data.Note)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            RestoreState(rollbackState);
            DeactivateOnIdle();
            throw;
        }

        await RetireReminderAsync(reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private TimerState? LoadRecorded()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(TimerState data) => _state.Value = _states.SerializeToArray(data);

    private byte[] SerializedState()
        => _state.Value is { } serialized ? serialized.ToArray() : [];

    private void RestoreState(byte[] serialized) => _state.Value = serialized;

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A timer command requires a command id.");
        }
    }

    private Task<Orleans.Runtime.IGrainReminder> RegisterReminderAsync(string reminderName, TimeSpan dueTime)
        => this.RegisterOrUpdateReminder(reminderName, dueTime, ReminderPeriod);

    private async Task RetireReminderAsync(string? reminderName)
    {
        if (reminderName is not null
            && await this.GetReminder(reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } reminder)
        {
            await this.UnregisterReminder(reminder).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private static string ReminderName(long generation)
        => string.Create(CultureInfo.InvariantCulture, $"{ReminderPrefix}{generation}");

    private static bool TryParseReminderName(string reminderName, out long generation)
    {
        generation = 0;
        return reminderName.StartsWith(ReminderPrefix, StringComparison.Ordinal)
            && long.TryParse(
                reminderName.AsSpan(ReminderPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out generation)
            && generation > 0;
    }
}

