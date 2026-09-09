using System.Text.Json;
using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Execution;

[GrainType("execution")]
public sealed class ExecutionNeuron : Neuron, IExecution, IExecutionKernel
{
    private const string StateName = "execution.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<ExecutionState> _states;

    public ExecutionNeuron(NeuronRuntime runtime)
        : base(runtime)
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<ExecutionState>>();
    }

    public Task<ExecutionProjection> LoadProjection()
    {
        var data = LoadRecorded()
            ?? throw new InvalidOperationException($"Execution '{Id}' has not been started.");
        return Task.FromResult(
            new ExecutionProjection(
                data.ExecutionId,
                data.Status,
                data.Workload,
                data.PromptBlocks));
    }

    public async Task HandleAsync(ReadExecution signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        await ReplyAsync(await LoadProjection().WaitAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(StartExecution signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(signal.CommandId);
        RequireMatchingExecution(signal.ExecutionId);

        var current = LoadRecorded();
        if (current is { Status: ExecutionStatus.Running })
        {
            throw new NeuronAuthorizationException(
                $"Execution '{Id}' is already active with status '{current.Status}'.");
        }

        Stage(new ExecutionState(
            signal.ExecutionId,
            ExecutionStatus.Running,
            signal.Workload));

        var context = GrainFactory.GetGrain<IExecutionContext>(
            EntityId.For<IExecutionContext>(Id.Owner, signal.ExecutionId.ToString()).ToGrainId());

        try
        {
            await context.Ensure(signal.ExecutionId)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            await AdmitRelatedContextAsync(context, signal.RelatedExecutions, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            var promptBlocks = await CollectPromptBlocksAsync(signal.Workload, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await context.ApplyDelta(new ContextDelta(
                    new ContextPath($"chat.turn.{signal.Workload.TurnId:N}"),
                    SchemaHash: "chat.turn.v1",
                    PayloadJson: JsonSerializer.Serialize(signal.Workload.UserText),
                    BlobRef: null))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            Stage(LoadRecorded()! with { PromptBlocks = promptBlocks });

            Stage(LoadRecorded()! with { Status = ExecutionStatus.Completed });
            await RecordOutgoingAsync(new ExecutionLifecycle(signal.ExecutionId, ExecutionStatus.Completed))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not NeuronAuthorizationException)
        {
            Stage(LoadRecorded()! with { Status = ExecutionStatus.Failed });
            await RecordOutgoingAsync(new ExecutionLifecycle(signal.ExecutionId, ExecutionStatus.Failed, ex.Message))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            throw new NeuronAuthorizationException($"Execution '{Id}' failed: {ex.Message}", ex);
        }
    }

    private ExecutionState? LoadRecorded()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(ExecutionState data) => _state.Value = _states.SerializeToArray(data);

    private async Task AdmitRelatedContextAsync(
        IExecutionContext context,
        IReadOnlyList<ExecutionId>? relatedExecutions,
        CancellationToken cancellationToken)
    {
        if (relatedExecutions is not { Count: > 0 })
        {
            return;
        }

        for (var i = 0; i < relatedExecutions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relatedId = relatedExecutions[i];
            var related = GrainFactory.GetGrain<IExecutionContext>(
                EntityId.For<IExecutionContext>(Id.Owner, relatedId.ToString()).ToGrainId());
            var state = await related.Read()
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (state?.Slots is not { Count: > 0 } slots)
            {
                continue;
            }

            for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                var slot = slots[slotIndex];
                await context.ApplyDelta(new ContextDelta(
                        slot.Path,
                        slot.Entry.SchemaHash,
                        slot.Entry.PayloadJson,
                        slot.Entry.BlobRef))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }
    }

    private async Task<IReadOnlyList<string>?> CollectPromptBlocksAsync(
        ChatTurnWorkload workload,
        CancellationToken cancellationToken)
    {
        var blocks = new List<string> { $"Current user turn: {workload.UserText}" };
        try
        {
            var turns = await GrainFactory.GetGrain<IChatKernel>(workload.ChatId.ToGrainId())
                .LoadTurnSnapshots()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (turns.Count > 0)
            {
                var start = Math.Max(0, turns.Count - 8);
                var lines = new List<string>(turns.Count - start);
                for (var i = start; i < turns.Count; i++)
                {
                    lines.Add($"- {turns[i].Status}: {turns[i].Text}");
                }

                blocks.Add("Recent chat turns:\n" + string.Join('\n', lines));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chat neuron may be absent when UI module is not loaded.
        }

        return blocks;
    }

    private void RequireMatchingExecution(ExecutionId executionId)
    {
        if (!string.Equals(executionId.ToString(), Id.Name, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException(
                $"Execution neuron '{Id}' refuses command for execution '{executionId}'.");
        }
    }

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("An execution command requires a command id.");
        }
    }
}
