using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Core;

// Trusted server adapter for chat ingress. Application protocols stay internal.
public interface IApplicationChatIngress
{
    Task<ApplicationChatAdmission?> AdmitAsync(OwnerId owner, PrincipalId principal,
        Guid signalId, Guid correlationId, string sourceId, string contract, string matchText,
        string payload, CancellationToken cancellationToken = default);
    Task<ApplicationChatResult> AwaitAsync(ApplicationChatAdmission admission, CancellationToken cancellationToken = default);
    Task<bool> HasFallbackAsync(OwnerId owner, PrincipalId principal, string sourceId,
        CancellationToken cancellationToken = default);
}

public sealed record ApplicationChatAdmission(OwnerId Owner, PrincipalId Principal,
    string ApplicationKey, Guid OperationId, string Revision);
public sealed record ApplicationChatResult(string Output, string ApplicationKey, string Revision);

internal sealed class ApplicationChatIngress(IGrainFactory grains) : IApplicationChatIngress
{
    public Task<bool> HasFallbackAsync(OwnerId owner, PrincipalId principal, string sourceId,
        CancellationToken cancellationToken = default)
    {
        RequirePrincipal(principal);
        return grains.GetGrain<IApplicationCatalog>($"{owner.Value}/{principal}")
            .HasEventRoute(sourceId, "chat.user-messaged/v1").WaitAsync(cancellationToken);
    }

    public async Task<ApplicationChatAdmission?> AdmitAsync(OwnerId owner, PrincipalId principal,
        Guid signalId, Guid correlationId, string sourceId, string contract, string matchText,
        string payload, CancellationToken cancellationToken = default)
    {
        RequirePrincipal(principal);
        if (sourceId != new NeuronId("usermessages", owner, "inbox").ToString() || contract != "chat.user-messaged/v1")
        {
            throw new InvalidOperationException("This adapter admits only the owner's composer user-message output.");
        }
        var route = await grains.GetGrain<IApplicationCatalog>($"{owner.Value}/{principal}")
            .Admit(signalId, correlationId, sourceId, contract, matchText, payload)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (route is null) { return null; }
        var admission = new ApplicationChatAdmission(owner, principal, route.ApplicationKey, signalId, route.Revision);
        await Kernel(admission).SubmitPinned(signalId, route.Revision, route.Operation.Key,
            route.Operation.RequestContract, route.Operation.ResponseContract, payload)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return admission;
    }

    public async Task<ApplicationChatResult> AwaitAsync(ApplicationChatAdmission admission, CancellationToken cancellationToken = default)
    {
        RequirePrincipal(admission.Principal);
        while (true)
        {
            var result = await Kernel(admission).Read(admission.OperationId).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result.Status == "completed")
            {
                return new(JsonSerializer.Deserialize<string>(result.Value!)!, admission.ApplicationKey, admission.Revision);
            }
            if (result.Status == "failed") { throw new InvalidOperationException($"Application chat operation failed: {result.Error}"); }
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private IApplicationKernel Kernel(ApplicationChatAdmission admission)
        => grains.GetGrain<IApplicationKernel>($"{admission.Owner.Value}/{admission.Principal}/{admission.ApplicationKey}");

    private static void RequirePrincipal(PrincipalId principal)
    {
        if (VerifiedActor.Current?.PrincipalId != principal)
        {
            throw new InvalidOperationException("Chat admission requires the verified message principal.");
        }
    }
}
