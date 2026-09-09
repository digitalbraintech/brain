namespace DigitalBrain.Product.Interactions;

public interface IUserActionContinuation
{
    Task CompleteAsync(AgentTurnContext context, string actionId, bool accepted, CancellationToken cancellationToken);
    Task<bool> IsWaitingAsync(AgentTurnContext context, string actionId, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
