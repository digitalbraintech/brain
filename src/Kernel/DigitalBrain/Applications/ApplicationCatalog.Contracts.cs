using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Core;

internal sealed partial class ApplicationCatalog
{
    public async Task<ApplicationRoute?> AdmitContract(Guid signalId, Guid correlationId,
        string implementedContract, string implementedInstance, string requestContract,
        string responseContract, string payload)
    {
        Authorize();
        if (signalId == Guid.Empty || correlationId == Guid.Empty)
        {
            throw new ArgumentException("A contract admission needs signal and correlation identities.");
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
        {
            correlationId.ToString(), implementedContract, implementedInstance,
            requestContract, responseContract, payload,
        }))));
        if (storage.State.ContractDecisions.TryGetValue(signalId, out var decision))
        {
            if (decision.Hash != hash)
            {
                throw new InvalidOperationException("A contract request identity cannot be reused with different content.");
            }
            return decision.Route;
        }

        var route = storage.State.Heads.SelectMany(head => head.Value.Operations.Select(operation =>
                new ApplicationRoute(head.Key, head.Value.Revision, operation)))
            .SingleOrDefault(candidate => candidate.Operation.ImplementedContract == implementedContract
                && candidate.Operation.ImplementedInstance == implementedInstance
                && candidate.Operation.RequestContract == requestContract
                && candidate.Operation.ResponseContract == responseContract);
        await Commit(storage.State with
        {
            ContractDecisions = new(storage.State.ContractDecisions) { [signalId] = new(hash, route) },
        });
        return route;
    }
}
