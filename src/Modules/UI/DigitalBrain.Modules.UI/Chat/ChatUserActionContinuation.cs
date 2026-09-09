using DigitalBrain.Chat;
using DigitalBrain.Product.Interactions;

namespace DigitalBrain.UI;

internal sealed class ChatUserActionContinuation(IGrainFactory grains) : IUserActionContinuation
{
    public async Task<bool> IsWaitingAsync(AgentTurnContext context, string actionId, CancellationToken cancellationToken)
    {
        var turns = await grains.GetGrain<IChatKernel>(context.Chat.ToGrainId())
            .LoadTurnSnapshots()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return turns.Any(turn => turn.CommandId == context.CommandId && turn.Status == ChatTurnStatus.WaitingForUser && turn.UserAction?.Id == actionId);
    }

    public Task CompleteAsync(
        AgentTurnContext context,
        string actionId,
        bool accepted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        cancellationToken.ThrowIfCancellationRequested();
        return grains.GetGrain<IChat>(context.Chat.ToGrainId())
            .HandleAsync(new CompleteUserAction(context, actionId, accepted), cancellationToken);
    }
}
