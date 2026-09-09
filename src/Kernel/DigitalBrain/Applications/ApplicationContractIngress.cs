using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Core;

public interface IApplicationContractIngress
{
    Task<ApplicationContractAdmission?> AdmitAsync(OwnerId owner, PrincipalId principal,
        Guid signalId, Guid correlationId, string implementedContract, string implementedInstance,
        string requestContract, string responseContract, string payload,
        CancellationToken cancellationToken = default);
    Task<string> AwaitAsync(ApplicationContractAdmission admission, CancellationToken cancellationToken = default);
}

public sealed record ApplicationContractAdmission(OwnerId Owner, PrincipalId Principal,
    string ApplicationKey, Guid OperationId, string Revision);

internal sealed class ApplicationContractIngress(IGrainFactory grains) : IApplicationContractIngress
{
    public async Task<ApplicationContractAdmission?> AdmitAsync(OwnerId owner, PrincipalId principal,
        Guid signalId, Guid correlationId, string implementedContract, string implementedInstance,
        string requestContract, string responseContract, string payload,
        CancellationToken cancellationToken = default)
    {
        RequirePrincipal(principal);
        if (!PrincipalPartition.TryParse(implementedInstance, out var instancePrincipal, out var localInstance)
            || instancePrincipal != principal)
        {
            throw new InvalidOperationException("The implemented neuron instance belongs to another principal.");
        }
        var route = await grains.GetGrain<IApplicationCatalog>($"{owner.Value}/{principal}")
            .AdmitContract(signalId, correlationId, implementedContract, localInstance,
                requestContract, responseContract, payload).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (route is null) { return null; }
        var admission = new ApplicationContractAdmission(owner, principal, route.ApplicationKey, signalId, route.Revision);
        await Kernel(admission).SubmitPinned(signalId, route.Revision, route.Operation.Key,
            route.Operation.RequestContract, route.Operation.ResponseContract, payload)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return admission;
    }

    public async Task<string> AwaitAsync(ApplicationContractAdmission admission, CancellationToken cancellationToken = default)
    {
        RequirePrincipal(admission.Principal);
        while (true)
        {
            var result = await Kernel(admission).Read(admission.OperationId).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result.Status == "completed") { return result.Value!; }
            if (result.Status == "failed")
            {
                throw new InvalidOperationException($"Application contract operation failed: {result.Error}");
            }
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private IApplicationKernel Kernel(ApplicationContractAdmission admission)
        => grains.GetGrain<IApplicationKernel>($"{admission.Owner.Value}/{admission.Principal}/{admission.ApplicationKey}");

    private static void RequirePrincipal(PrincipalId principal)
    {
        if (VerifiedActor.Current?.PrincipalId != principal)
        {
            throw new InvalidOperationException("Contract admission requires the verified message principal.");
        }
    }
}
