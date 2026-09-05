using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.Product.Interactions;

public interface IUserActionSource
{
    UserActionRequest? Find(OwnerId owner, CommandId commandId);
    UserActionRequest? Recover(AgentTurnContext context, UserActionRequest action) => null;
    SpecialistContinuation? ResolveSpecialistContinuation(AgentTurnContext context, string actionId) => null;
    void Cancel(AgentTurnContext context) { }
}
