using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("github.refresh-repository")]
internal sealed record RefreshRepository([property: Id(0)] string BindingId, [property: Id(1)] string DeliveryId, [property: Id(2)] string BindingRevision, [property: Id(3)] int? Number = null, [property: Id(4)] bool Revoke = false, [property: Id(5)] string? Sha = null, [property: Id(6)] string? Action = null, [property: Id(7)] bool AuthenticatedCallback = false) : Signal;
[GenerateSerializer, Alias("github.repository-state")]
internal sealed record RepositoryState
{
    [Id(0)]
    public Dictionary<int, PullRequestSnapshot> PullRequests { get; init; } = [];

    [Id(3)]
    public string? BindingRevision { get; init; }

    [Id(4)]
    public bool Revoked { get; init; }

    [Id(6)]
    public DateTimeOffset? LastWebhookAt { get; init; }
}

[GenerateSerializer, Alias("github.repository-observed")]
internal sealed record RepositoryObserved([property: Id(0)] PullRequestSnapshot[] Snapshots, [property: Id(1)] bool Revoke = false) : Signal;
[GenerateSerializer, Alias("github.repository-query")]
internal sealed record RepositoryQuery([property: Id(0)] SignalDelivery Request) : Signal;
[GenerateSerializer, Alias("github.repository-query-result")]
internal sealed record RepositoryQueryResult([property: Id(0)] SignalDelivery Request, [property: Id(1)] Signal Response) : Signal;
[Alias("github.repository-projection")]
internal interface IRepositoryProjection : IGrainWithStringKey
{
    Task<PullRequestSnapshot[]> ReadProjectionAsync();
    Task<DateTimeOffset?> ReadLastWebhookAsync();
}

[GrainType("repository")]
internal sealed class Repository : WebhookNeuron, IRepository, IRepositoryProjection
{
    private readonly GitHubRepositoryBindings _bindings;
    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<RepositoryState> _serializer;
    private RepositoryState? _loaded;
    private DateTimeOffset _lastReconciled;
    public Repository(NeuronRuntime runtime, GitHubRepositoryBindings bindings) : base(runtime)
    {
        _bindings = bindings;
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>("github.repository");
        _serializer = ServiceProvider.GetRequiredService<Serializer<RepositoryState>>();
    }

    private GitHubRepositoryBinding Binding => _bindings.TryFor(Id, out var binding) ? binding : throw new NeuronAuthorizationException("The GitHub source has no authorized repository connection.");
    protected override ActorContext SourceActor => new(Binding.Principal, "github-webhook");
    protected override string SourceEpoch => Binding.Revision;
    protected override bool SourceAvailable => Binding.Enabled && (!Load().Revoked || Load().BindingRevision != SourceEpoch);
    protected override string? SourceDetail => SourceAvailable ? null : "GitHub repository access must be connected or restored.";

    protected override void AuthorizeReceipt(WebhookReceipt receipt)
    {
        RequireActor();
        if (receipt.Input is RefreshRepository refresh)
        {
            if (refresh.BindingId != Binding.Id || refresh.BindingRevision != Binding.Revision)
            {
                throw new NeuronAuthorizationException("The webhook does not match its repository connection.");
            }
        }
        else if (receipt.Input is not RepositoryQuery)
        {
            throw new NeuronAuthorizationException("This repository source refuses unsupported receipt data.");
        }
    }

    protected override bool CanAccept(WebhookReceipt receipt) => SourceAvailable || receipt.Input is RefreshRepository { Revoke: true };
    protected override bool CanDeliver(Signal signal) => SourceAvailable || signal is RepositoryAccessRevoked or WebhookReceived { Fact: RepositoryAccessRevoked };
    protected override bool CanCoalesce(WebhookReceipt first, WebhookReceipt next) => first.Input is RefreshRepository left && next.Input is RefreshRepository right && !left.Revoke && !right.Revoke && left.Number == right.Number && left.Sha == right.Sha && left.Action is not ("opened" or "reopened" or "closed" or "ping") && right.Action is not ("opened" or "reopened" or "closed" or "ping");
    protected override Action? StageAcceptance(WebhookReceipt receipt)
    {
        var previous = Load();
        if (receipt.Input is RefreshRepository { Revoke: true })
        {
            Stage(previous with { Revoked = true, BindingRevision = SourceEpoch });
        }
        else if (previous.BindingRevision != SourceEpoch)
        {
            Stage(previous with { Revoked = false, BindingRevision = SourceEpoch, LastWebhookAt = receipt.Input is RefreshRepository { AuthenticatedCallback: true } ? receipt.AcceptedAt : null });
        }
        else if (receipt.Input is RefreshRepository { AuthenticatedCallback: true })
        {
            Stage(previous with { LastWebhookAt = receipt.AcceptedAt });
        }
        else
        {
            return null;
        }

        return () => Stage(previous);
    }

    protected override void ReceiptAccepted(WebhookReceipt receipt)
    {
        if (receipt.Input is RefreshRepository { Revoke: true })
        {
            Binding.Revoke();
        }
    }

    protected override async Task OnSourceRecoveryAsync(CancellationToken cancellationToken)
    {
        var binding = Binding;
        if (!binding.RecoveryComplete)
        {
            if (Load().Revoked && Load().BindingRevision == binding.Revision)
            {
                binding.Revoke();
            }

            binding.CompleteRecovery();
        }

        if (Load().Revoked && Load().BindingRevision == binding.Revision)
        {
            binding.Revoke();
        }

        if (binding.Enabled && (await ReadSynapses()).Count > 0 && !HasPendingInput<RefreshRepository>() && TimeProvider.GetUtcNow() - _lastReconciled >= TimeSpan.FromMinutes(5))
        {
            await ReconcileAsync();
        }
    }

    protected override Task OnSubscriptionsChangedAsync() => SourceAvailable && !HasPendingInput<RefreshRepository>() ? ReconcileAsync() : Task.CompletedTask;
    private async Task ReconcileAsync()
    {
        if ((await ReadSynapses()).Count == 0)
        {
            return;
        }

        _lastReconciled = TimeProvider.GetUtcNow();
        var delivery = $"reconcile:{SourceEpoch}:{_lastReconciled.ToUnixTimeSeconds() / 300}";
        await AcceptAsync(new(delivery, GitHubRepositorySource.Hash(delivery), SourceEpoch, new RefreshRepository(Binding.Id, delivery, SourceEpoch), _lastReconciled));
    }

    public async Task<PullRequestSnapshot[]> ReadProjectionAsync()
    {
        RequireActor();
        await RefreshSourceEpochAsync();
        return Load().PullRequests.Values.ToArray();
    }

    public Task<DateTimeOffset?> ReadLastWebhookAsync()
    {
        RequireActor();
        return Task.FromResult(Load().BindingRevision == SourceEpoch ? Load().LastWebhookAt : null);
    }

    public Task HandleAsync(ReadPullRequest signal, CancellationToken cancellationToken)
    {
        RequireActor();
        return signal.Refresh && SourceAvailable ? EnqueueQueryAsync() : ReplyAsync(new PullRequestRead(Load().PullRequests.GetValueOrDefault(signal.Number), SourceAvailable));
    }

    public Task HandleAsync(ReadPullRequests signal, CancellationToken cancellationToken)
    {
        RequireActor();
        return ReplyAsync(new PullRequestsRead(Load().PullRequests.Values.OrderBy(item => item.Number).ToArray(), SourceAvailable));
    }

    public Task HandleAsync(ReadReviewEvidence signal, CancellationToken cancellationToken) => EnqueueQueryAsync();
    public Task HandleAsync(ReadRequiredChecks signal, CancellationToken cancellationToken) => EnqueueQueryAsync();
    private async Task EnqueueQueryAsync()
    {
        RequireActor();
        var request = CurrentDelivery ?? throw new InvalidOperationException("A repository query requires its signal context.");
        var id = "query:" + request.SignalId;
        var accepted = await AcceptAsync(new(id, GitHubRepositorySource.Hash(id), SourceEpoch, new RepositoryQuery(request), TimeProvider.GetUtcNow()));
        if (accepted is not (WebhookAcceptance.Accepted or WebhookAcceptance.Duplicate))
        {
            throw new InvalidOperationException("The repository query could not be accepted durably.");
        }
    }

    protected override WebhookApplication ApplyReceipt(WebhookReceipt receipt, Signal[] results)
    {
        var previous = Load();
        var next = previous with
        {
            PullRequests = new(previous.PullRequests),
            BindingRevision = SourceEpoch
        };
        var facts = new List<WebhookFact>();
        foreach (var result in results)
        {
            if (result is RepositoryQueryResult query)
            {
                facts.Add(new("reply:" + query.Request.SignalId, query.Response, query.Request));
                if (query.Response is PullRequestRead { Snapshot: { } snapshot })
                {
                    Apply(next, snapshot, facts);
                }
            }
            else if (result is RepositoryObserved observation)
            {
                if (observation.Revoke || !Binding.Enabled)
                {
                    facts.Add(new("revoked:" + SourceEpoch, new RepositoryAccessRevoked(Binding.Id, Id, "revoked:" + SourceEpoch)));
                    next = next with
                    {
                        Revoked = true
                    };
                }
                else
                {
                    foreach (var snapshot in observation.Snapshots)
                    {
                        Apply(next, snapshot, facts, receipt.Input as RefreshRepository);
                    }
                }
            }
        }

        Stage(next);
        return new(facts, () => Stage(previous));
    }

    private void Apply(RepositoryState state, PullRequestSnapshot snapshot, List<WebhookFact> facts, RefreshRepository? trigger = null)
    {
        if (snapshot.RepositoryId != Binding.RepositoryId || snapshot.Number <= 0)
        {
            throw new NeuronAuthorizationException("Observed evidence belongs to a different repository.");
        }

        var previous = state.PullRequests.GetValueOrDefault(snapshot.Number);
        if (previous is null && state.PullRequests.Count >= 512)
        {
            var expired = state.PullRequests.Values.Where(item => !item.IsOpen).OrderBy(item => item.ObservedAt).FirstOrDefault() ?? throw new InvalidOperationException("The repository observation capacity is full.");
            state.PullRequests.Remove(expired.Number);
        }

        state.PullRequests[snapshot.Number] = snapshot;
        var change = (PullRequestChange)0;
        var eventId = $"{SourceEpoch}:{snapshot.Number}:{snapshot.Revision}:{snapshot.CiRevision}";
        if (previous is null || !previous.IsOpen && snapshot.IsOpen)
        {
            change |= snapshot.IsOpen ? PullRequestChange.Opened : PullRequestChange.Closed;
        }
        else if (previous.IsOpen && !snapshot.IsOpen)
        {
            change |= PullRequestChange.Closed;
        }
        else if (previous.Revision != snapshot.Revision || previous.IsDraft != snapshot.IsDraft)
        {
            change |= PullRequestChange.Updated;
        }

        if (previous is not null && previous.CiRevision != snapshot.CiRevision)
        {
            change |= PullRequestChange.Checks;
        }

        // A lifecycle edge remains observable even if the authoritative read already sees
        // a later state. Its snapshot always remains current evidence, never fabricated history.
        if (trigger?.Number == snapshot.Number && trigger.Action is "opened" or "reopened" or "closed")
        {
            change |= trigger.Action == "closed" ? PullRequestChange.Closed : PullRequestChange.Opened;
            eventId = $"{SourceEpoch}:{snapshot.Number}:callback:{trigger.DeliveryId}";
        }

        if (change == 0)
        {
            return;
        }

        facts.Add(new(eventId, new PullRequestChanged(snapshot, change, eventId)));
    }
    private RepositoryState Load() => _loaded ??= _state.Value is { Length: > 0 } bytes
        ? _serializer.Deserialize(bytes) ?? new()
        : new();
    private void Stage(RepositoryState next)
    {
        _loaded = next;
        _state.Value = _serializer.SerializeToArray(next);
    }
}
