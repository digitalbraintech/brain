using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using Orleans.Runtime;

namespace DigitalBrain.Core;

[GrainType("application-catalog")]
internal sealed partial class ApplicationCatalog(
    [PersistentState("applications", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ApplicationCatalogState> storage,
    IGrainFactory grains,
    IEnumerable<ApplicationNeuronEventRegistration> eventSources,
    IEnumerable<ApplicationNeuronInputRegistration> neuronInputs)
    : Grain, IApplicationCatalog
{
    public async Task<Guid[]> Activate(string applicationKey, ApplicationManifest manifest)
    {
        var owner = Authorize();
        foreach (var operation in manifest.Operations)
        {
            if (operation.Trigger is not { } trigger) { continue; }
            if (trigger.SourceId != new NeuronId("usermessages", owner, "inbox").ToString()
                || trigger.Contract != "chat.user-messaged/v1" || operation.RequestContract != trigger.Contract
                || operation.ResponseContract != "system.string/v1" || string.IsNullOrWhiteSpace(trigger.Text))
            {
                throw new InvalidOperationException("The application declares an unsupported chat input.");
            }
        }
        var heads = new Dictionary<string, ApplicationManifest>(storage.State.Heads) { [applicationKey] = manifest };
        ValidateEvents(applicationKey, manifest, heads);
        var triggers = heads.Values.SelectMany(x => x.Operations).Select(x => x.Trigger)
            .OfType<ApplicationTextTrigger>().ToArray();
        for (var index = 0; index < triggers.Length; index++)
        {
            if (triggers.Skip(index + 1).Any(other => Overlaps(triggers[index], other)))
            {
                throw new InvalidOperationException("Two application rules cannot claim the same chat input.");
            }
        }
        var duplicateContract = heads.Values.SelectMany(x => x.Operations)
            .Where(x => x.ImplementedContract is not null)
            .GroupBy(x => (x.ImplementedContract, x.ImplementedInstance)).Any(x => x.Count() > 1);
        if (duplicateContract) { throw new InvalidOperationException("Two applications cannot implement the same neuron instance."); }
        var revisions = new Dictionary<string, ApplicationManifest>(storage.State.Revisions)
        {
            [$"{applicationKey}/{manifest.Revision}"] = manifest,
        };
        var cursors = await InitializeEventCursors(owner, applicationKey, manifest);
        await Commit(storage.State with { Heads = heads, Revisions = revisions, EventSourceCursors = cursors });
        return await CatchUpRootActivation(owner, applicationKey, manifest);
    }

    public Task<string> Head(string applicationKey)
    {
        Authorize();
        return Task.FromResult(storage.State.Heads.TryGetValue(applicationKey, out var head)
            ? head.Revision : throw new InvalidOperationException("Install the application before invoking it."));
    }

    public Task<bool> HasEventRoute(string sourceId, string contract)
    {
        Authorize();
        return Task.FromResult(storage.State.Heads.Values.Any(manifest =>
            (manifest.Connections ?? []).Any(connection => connection.Source.SourceKind == "neuron"
                && connection.Source.SourceId == sourceId && connection.Source.Contract == contract)));
    }

    public async Task<ApplicationRoute?> Admit(Guid signalId, Guid correlationId, string sourceId, string contract, string text, string payload)
    {
        Authorize();
        if (signalId == Guid.Empty || correlationId == Guid.Empty)
        {
            throw new ArgumentException("A chat admission needs signal and correlation identities.");
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { correlationId.ToString(), sourceId, contract, text, payload }))));
        ApplicationRoute? route;
        if (storage.State.Decisions.TryGetValue(signalId, out var decision))
        {
            if (decision.Hash != hash) { throw new InvalidOperationException("A chat event identity cannot be reused with different content."); }
            route = decision.Route;
        }
        else
        {
            if (storage.State.Decisions.Count >= 100000) { throw new InvalidOperationException("The chat admission retention limit is reached."); }
            route = storage.State.Heads.SelectMany(head => head.Value.Operations.Select(operation =>
                    new ApplicationRoute(head.Key, head.Value.Revision, operation)))
                .SingleOrDefault(candidate => candidate.Operation.Trigger is { } trigger
                    && trigger.SourceId == sourceId && trigger.Contract == contract
                    && (trigger.ContainsIgnoreCase
                        ? text.Contains(trigger.Text, StringComparison.OrdinalIgnoreCase)
                        : trigger.Text == text));
            await Commit(storage.State with
            {
                Decisions = new(storage.State.Decisions) { [signalId] = new(hash, route) },
            });
        }
        return route;
    }

    private OwnerId Authorize()
    {
        var actor = VerifiedActor.Current ?? throw new InvalidOperationException("An authenticated application principal is required.");
        var key = this.GetPrimaryKeyString().Split('/');
        if (key.Length != 2 || key[1] != actor.PrincipalId.ToString())
        {
            throw new InvalidOperationException("The application catalog belongs to another principal.");
        }
        return new(key[0]);
    }

    private static bool Overlaps(ApplicationTextTrigger left, ApplicationTextTrigger right)
    {
        if (left.SourceId != right.SourceId || left.Contract != right.Contract) { return false; }
        // A message can contain both phrases, even when neither phrase contains the other.
        if (left.ContainsIgnoreCase && right.ContainsIgnoreCase) { return true; }
        if (left.ContainsIgnoreCase) { return right.Text.Contains(left.Text, StringComparison.OrdinalIgnoreCase); }
        if (right.ContainsIgnoreCase) { return left.Text.Contains(right.Text, StringComparison.OrdinalIgnoreCase); }
        return left.Text == right.Text;
    }

    private async Task Commit(ApplicationCatalogState next)
    {
        var previous = storage.State;
        storage.State = next;
        try { await storage.WriteStateAsync(); }
        catch { storage.State = previous; throw; }
    }
}

[GenerateSerializer, Alias("db.application-catalog-state")]
internal sealed record ApplicationCatalogState
{
    [Id(0)] public Dictionary<string, ApplicationManifest> Heads { get; init; } = [];
    [Id(1)] public Dictionary<Guid, ApplicationChatDecision> Decisions { get; init; } = [];
    [Id(2)] public Dictionary<string, ApplicationManifest> Revisions { get; init; } = [];
    [Id(3)] public Dictionary<Guid, ApplicationStoredEvent> Events { get; init; } = [];
    [Id(4)] public Dictionary<Guid, ApplicationStoredDelivery> EventDeliveries { get; init; } = [];
    [Id(5)] public Dictionary<Guid, ApplicationContractDecision> ContractDecisions { get; init; } = [];
    [Id(6)] public Dictionary<string, bool> RootActivationConnections { get; init; } = [];
    [Id(7)] public Dictionary<string, long> EventSourceCursors { get; init; } = [];
    [Id(8)] public long NextApplicationEventSequence { get; init; }
}

[GenerateSerializer, Alias("db.application-chat-decision")]
internal sealed record ApplicationChatDecision(
    [property: Id(0)] string Hash,
    [property: Id(1)] ApplicationRoute? Route);

[GenerateSerializer, Alias("db.application-contract-decision")]
internal sealed record ApplicationContractDecision(
    [property: Id(0)] string Hash,
    [property: Id(1)] ApplicationRoute? Route);
