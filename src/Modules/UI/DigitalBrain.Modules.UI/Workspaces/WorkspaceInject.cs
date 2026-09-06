using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.UI;

public sealed class WorkspaceInject(IDigitalBrain brain, IGrainFactory grains) : IWorkspaceInject
{
    public async Task<CommandId> InjectUserMessage(
        string workspaceName,
        string text,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        if (VerifiedActor.Current is { } verified && verified.PrincipalId != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException("User messages must be injected as the verified actor.");
        }

        var index = brain.GetEntity<IWorkspaceIndex>(IWorkspaceIndex.DefaultInstanceName);
        var record = await index.Find(workspaceName).ConfigureAwait(false);
        if (record is null)
        {
            if (!string.Equals(workspaceName, "main", StringComparison.Ordinal))
            {
                throw new NeuronAuthorizationException($"Unknown workspace '{workspaceName}'.");
            }

            await index.Ensure("main", "Main").ConfigureAwait(false);
            record = await index.Find("main").ConfigureAwait(false)
                ?? throw new InvalidOperationException("Workspace 'main' could not be created.");
        }

        var command = CommandId.New();
        var inbox = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var correlation = new CorrelationId(Guid.Parse(record.CorrelationId));
        var session = grains.GetGrain<IBrainNeuron>(IBrainNeuron.ForOwner(brain.Owner).ToGrainId());
        await session.SendWithCorrelation(
                inbox.Id,
                new UserMessaged(command, inbox.Id, text, actor),
                correlation,
                cancellationToken)
            .ConfigureAwait(false);
        return command;
    }
}
