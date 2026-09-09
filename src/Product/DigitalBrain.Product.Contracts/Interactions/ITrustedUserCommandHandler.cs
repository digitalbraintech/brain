namespace DigitalBrain.Product.Interactions;

// Invoked only by the authenticated chat edge, never exposed as an agent tool.
public interface ITrustedUserCommandHandler
{
    Task<string?> HandleAsync(string originalUserText, CancellationToken cancellationToken);
    string? ResponseFor(AgentTurnContext context);
    void ResponsePublished(AgentTurnContext context, string response);
}
