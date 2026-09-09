using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class TurnBoundFunction(AIFunction capability, TaskScheduler turnScheduler) : DelegatingAIFunction(capability)
{
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        => new(Task.Factory.StartNew(
            () => base.InvokeCoreAsync(arguments, cancellationToken).AsTask(),
            cancellationToken,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap());
}
