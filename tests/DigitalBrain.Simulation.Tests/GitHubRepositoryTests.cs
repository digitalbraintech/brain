using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Sdk;
using DigitalBrain.Sdk.Webhooks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Simulation.Tests;
public sealed class GitHubRepositoryTests
{
    [Fact]
    public async Task Shared_app_ping_without_identity_hints_is_routed_only_by_verified_signature()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var body = Encoding.UTF8.GetBytes("{\"zen\":\"fixture\",\"hook\":{\"type\":\"App\",\"active\":true},\"hook_id\":42}");
        var request = new WebhookRequest(body, new Dictionary<string, string[]>
        {
            ["X-GitHub-Event"] = ["ping"], ["X-GitHub-Delivery"] = [Guid.NewGuid().ToString()],
            ["X-Hub-Signature-256"] = ["sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(scenario.Binding.WebhookSecret), body))]
        });
        var shared = new GitHubSharedWebhookHandler(scenario.Registry, scenario.Simulation.Grains);
        Assert.Equal(WebhookAcceptance.Accepted, await shared.HandleAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Unauthorized, await shared.HandleAsync(request with { Body = Encoding.UTF8.GetBytes("{}") }, TestContext.Current.CancellationToken));
        Assert.NotNull(await scenario.Simulation.Grains.GetGrain<IRepositoryProjection>(scenario.Repository.Id.ToGrainId()).ReadLastWebhookAsync());
        await scenario.DrainAsync();
        Assert.Equal(0, scenario.Source.Reads);
    }

    [Fact]
    public async Task Replacement_connection_drops_queued_facts_and_receiver_rejects_late_old_envelope()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var subscriber = scenario.Subscriber("epoch");
        var receiver = scenario.Simulation.Grains.GetGrain<INeuronGrain>(subscriber.Id.ToGrainId());
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        var initialFence = Assert.IsType<WebhookWork>(await scenario.Ingress.ClaimAsync());
        await receiver.FenceSourceEpoch(scenario.Repository.Id, initialFence.FenceEpoch!.Value);
        await scenario.Ingress.AcknowledgeAsync(initialFence.Lease, true);
        var receipt = Assert.IsType<WebhookWork>(await scenario.Ingress.ClaimAsync());
        await scenario.Ingress.CompleteAsync(receipt.Lease, await scenario.Processor.ProcessAsync(scenario.Repository.Id, receipt.Receipt!, TestContext.Current.CancellationToken));
        var oldDelivery = Assert.IsType<WebhookWork>(await scenario.Ingress.ClaimAsync());
        Assert.NotNull(oldDelivery.Delivery?.SourceEpoch);
        var replacement = new GitHubRepositoryBinding(scenario.Binding.Id, scenario.Binding.Owner, scenario.Binding.Principal, 42, 43, 44, "owner", "repository", "fixture-private-key", "fixture-webhook-secret", authorizationEpoch: "replacement-epoch");
        scenario.Registry.Add(replacement);
        var fence = Assert.IsType<WebhookWork>(await scenario.Ingress.ClaimAsync());
        Assert.True(fence.FenceEpoch > oldDelivery.Delivery!.SourceEpoch);
        Assert.Null(await scenario.Ingress.ClaimAsync());
        await receiver.FenceSourceEpoch(scenario.Repository.Id, fence.FenceEpoch!.Value);
        await scenario.Ingress.AcknowledgeAsync(fence.Lease, true);
        Assert.Equal(DeliveryOutcome.Handled, await receiver.Deliver(oldDelivery.Delivery, TestContext.Current.CancellationToken));
        Assert.Equal(0, (await scenario.ReadAsync(subscriber)).Changed);
        Assert.Null(await scenario.Ingress.ClaimAsync());
        scenario.Source.Snapshot = scenario.Source.Snapshot with
        {
            CiRevision = "after-reconnect"
        };
        await scenario.Ingress.AcceptAsync(new("new-authority", GitHubRepositorySource.Hash("new-authority"), replacement.Revision, new RefreshRepository(replacement.Id, "new-authority", replacement.Revision, 1), DateTimeOffset.UtcNow));
        await scenario.DrainAsync();
        Assert.Equal(1, (await scenario.ReadAsync(subscriber)).Changed);
        await receiver.Deliver(oldDelivery.Delivery, TestContext.Current.CancellationToken);
        Assert.Equal(1, (await scenario.ReadAsync(subscriber)).Changed);
    }

    [Fact]
    public async Task Revocation_fences_queued_observation_but_delivers_revocation_in_new_generation()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var subscriber = scenario.Subscriber("revocation");
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, RepositoryAccessRevoked>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await scenario.DrainAsync();
        Assert.Equal(1, (await scenario.ReadAsync(subscriber)).Changed);
        scenario.Source.Snapshot = scenario.Source.Snapshot with
        {
            CiRevision = "queued-before-revoke"
        };
        await scenario.AcceptAsync("queued");
        var observation = Assert.IsType<WebhookWork>(await scenario.Ingress.ClaimAsync());
        await scenario.Ingress.CompleteAsync(observation.Lease, await scenario.Processor.ProcessAsync(scenario.Repository.Id, observation.Receipt!, TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString(), eventName: "installation", action: "deleted"), TestContext.Current.CancellationToken));
        await scenario.DrainAsync();
        var received = await scenario.ReadAsync(subscriber);
        Assert.Equal(1, received.Changed);
        Assert.Equal(1, received.Revoked);
    }

    [Fact]
    public async Task Shared_endpoint_retries_every_scope_without_reaccepting_the_successful_scope()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        var other = new GitHubRepositoryBinding("second", scenario.Binding.Owner, PrincipalId.New(), 42, 43, 44, "owner", "repository", "fixture-private-key", "fixture-webhook-secret");
        scenario.Registry.Add(other);
        other.Revoke();
        var handler = new GitHubSharedWebhookHandler(scenario.Registry, scenario.Simulation.Grains);
        var request = scenario.Signed(Guid.NewGuid().ToString());
        Assert.Equal(WebhookAcceptance.Unavailable, await handler.HandleAsync(request, TestContext.Current.CancellationToken));
        using (VerifiedActor.Enter(scenario.Actor))
        {
            Assert.Equal(1, (await scenario.Repository.RequestAsync(new ReadWebhook(), TestContext.Current.CancellationToken)).PendingReceipts);
        }

        var restored = new GitHubRepositoryBinding("second", other.Owner, other.Principal, 42, 43, 44, "owner", "repository", "fixture-private-key", "fixture-webhook-secret", authorizationEpoch: Guid.NewGuid().ToString("N"));
        scenario.Registry.Add(restored);
        Assert.Equal(WebhookAcceptance.Accepted, await handler.HandleAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Duplicate, await handler.HandleAsync(request, TestContext.Current.CancellationToken));
        using (VerifiedActor.Enter(scenario.Actor))
        {
            Assert.Equal(1, (await scenario.Repository.RequestAsync(new ReadWebhook(), TestContext.Current.CancellationToken)).PendingReceipts);
        }

        using (VerifiedActor.Enter(new(other.Principal, "second-owner")))
        {
            var id = NeuronId.For<IRepository>(other.Owner, other.InstanceName);
            var second = scenario.Simulation.Grains.GetGrain<IAuthenticatedWebhookIngress>(id.ToGrainId());
            Assert.NotNull((await second.ClaimAsync())?.Receipt);
            Assert.Null(await second.ClaimAsync());
        }
    }

    [Fact]
    public async Task Equivalent_refresh_burst_performs_one_provider_read_and_retains_each_receipt_identity()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        for (var index = 0; index < 12; index++)
        {
            Assert.Equal(WebhookAcceptance.Accepted, await scenario.AcceptAsync("refresh-" + index));
        }

        await scenario.DrainAsync();
        Assert.Equal(1, scenario.Source.Reads);
        for (var index = 0; index < 12; index++)
        {
            Assert.Equal(WebhookAcceptance.Duplicate, await scenario.AcceptAsync("refresh-" + index));
        }

        Assert.Null(await scenario.Ingress.ClaimAsync());
    }

    [Fact]
    public async Task Accepted_opening_edge_survives_a_later_closed_snapshot()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        scenario.Source.Snapshot = scenario.Source.Snapshot with
        {
            IsOpen = false
        };
        var subscriber = scenario.Subscriber("lifecycle");
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await scenario.DrainAsync();
        var request = scenario.Signed(Guid.NewGuid().ToString(), action: "opened");
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(request, TestContext.Current.CancellationToken));
        await scenario.DrainAsync();
        var journal = await scenario.Query(subscriber.Id).ReadJournal(JournalKind.Incoming, 0);
        var opening = Assert.Single(journal.Delta.Select(item => item.Signal).OfType<PullRequestChanged>(), item => item.Change.HasFlag(PullRequestChange.Opened));
        Assert.False(opening.Snapshot.IsOpen);
        Assert.Equal(WebhookAcceptance.Duplicate, await scenario.Handler.HandleAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Authenticated_ping_records_readiness_without_repository_IO()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var projection = scenario.Simulation.Grains.GetGrain<IRepositoryProjection>(scenario.Repository.Id.ToGrainId());
        Assert.Null(await projection.ReadLastWebhookAsync());
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString(), eventName: "ping"), TestContext.Current.CancellationToken));
        Assert.NotNull(await projection.ReadLastWebhookAsync());
        await scenario.DrainAsync();
        Assert.Equal(0, scenario.Source.Reads);
    }

    [Fact]
    public async Task Signed_webhook_is_durable_idempotent_and_rejects_conflicting_or_foreign_payloads()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        var delivery = Guid.NewGuid().ToString();
        var request = scenario.Signed(delivery);
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Duplicate, await scenario.Handler.HandleAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Conflict, await scenario.Handler.HandleAsync(scenario.Signed(delivery, number: 2), TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Unauthorized, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString(), repositoryId: 999), TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Unauthorized, await scenario.Handler.HandleAsync(request with { Body = Encoding.UTF8.GetBytes("{}") }, TestContext.Current.CancellationToken));
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var pending = Assert.IsType<WebhookReceipt>((await scenario.Ingress.ClaimAsync())?.Receipt);
        Assert.Equal(delivery, pending.DeliveryId);
        Assert.Equal(1, Assert.IsType<RefreshRepository>(pending.Input).Number);
        Assert.Equal(0, scenario.Source.Reads);
    }

    [Fact]
    public async Task Receipt_and_projection_read_finish_while_remote_repository_request_is_blocked()
    {
        var source = new GitHubFakeSource
        {
            Block = true
        };
        await using var scenario = await GitHubScenario.StartAsync(runWorker: true, restoredSource: source);
        using var actor = VerifiedActor.Enter(scenario.Actor);
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString()), TestContext.Current.CancellationToken));
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString()), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var read = await scenario.Repository.RequestAsync(new ReadPullRequest(1), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Null(read.Snapshot);
            Assert.False(source.Release.Task.IsCompleted);
        }
        finally
        {
            source.Release.TrySetResult();
        }

        await GitHubScenario.EventuallyAsync(async () => (await scenario.Repository.RequestAsync(new ReadPullRequest(1), TestContext.Current.CancellationToken)).Snapshot is not null);
    }

    [Fact]
    public async Task Canonical_repository_publishes_typed_facts_and_unsubscribe_stops_only_the_selected_route()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var subscriber = scenario.Subscriber("first");
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await scenario.DrainAsync();
        Assert.Equal(1, (await scenario.ReadAsync(subscriber)).Changed);
        await scenario.AcceptAsync("same-state");
        await scenario.DrainAsync();
        Assert.Equal(1, (await scenario.ReadAsync(subscriber)).Changed);
        scenario.Source.Snapshot = scenario.Source.Snapshot with
        {
            CiRevision = "green"
        };
        await scenario.AcceptAsync("checks");
        await scenario.DrainAsync();
        Assert.Equal(2, (await scenario.ReadAsync(subscriber)).Changed);
        var incoming = await scenario.Query(subscriber.Id).ReadJournal(JournalKind.Incoming, 0);
        Assert.All(incoming.Delta.Where(item => item.Signal is PullRequestChanged), fact =>
        {
            Assert.Equal(scenario.Repository.Id, fact.Caller);
            Assert.Equal(scenario.Actor.PrincipalId, fact.Principal);
        });
        Assert.DoesNotContain(incoming.Delta, item => item.Signal is WebhookReceived);
        await subscriber.UnsubscribeFromAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        scenario.Source.Snapshot = scenario.Source.Snapshot with
        {
            CiRevision = "rerun"
        };
        await scenario.AcceptAsync("unsubscribed");
        await scenario.DrainAsync();
        Assert.Equal(2, (await scenario.ReadAsync(subscriber)).Changed);
    }

    [Fact]
    public async Task Blocked_recipient_does_not_delay_healthy_recipient_or_next_provider_observation()
    {
        await using var scenario = await GitHubScenario.StartAsync(runWorker: true, restoredSource: new GitHubFakeSource { Block = true });
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var blocked = scenario.Subscriber("blocked");
        var healthy = scenario.Subscriber("healthy");
        scenario.Gate.Blocked = blocked.Id;
        await blocked.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await healthy.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await scenario.AcceptAsync("initial");
        scenario.Source.Release.TrySetResult();
        await scenario.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        try
        {
            await GitHubScenario.EventuallyAsync(async () => (await scenario.ReadAsync(healthy)).Changed == 1, seconds: 8);
            scenario.Source.Snapshot = scenario.Source.Snapshot with
            {
                CiRevision = "green"
            };
            await scenario.AcceptAsync("next");
            await GitHubScenario.EventuallyAsync(async () => (await scenario.ReadAsync(healthy)).Changed == 2, seconds: 8);
            Assert.False(scenario.Gate.Release.Task.IsCompleted);
        }
        finally
        {
            scenario.Gate.Release.TrySetResult();
        }

        await GitHubScenario.EventuallyAsync(async () => (await scenario.ReadAsync(blocked)).Changed == 2);
        Assert.Equal(2, (await scenario.ReadAsync(healthy)).Changed);
    }

    [Fact]
    public async Task New_silo_recovers_accepted_receipt_without_redelivery_and_retains_dedupe_tombstone()
    {
        var journal = new VolatileJournalStorageProvider(Options.Create(new JournaledStateManagerOptions()));
        var first = await GitHubScenario.StartAsync(journal: journal);
        var binding = first.Binding;
        var source = first.Source;
        var delivery = Guid.NewGuid().ToString();
        try
        {
            Assert.Equal(WebhookAcceptance.Accepted, await first.Handler.HandleAsync(first.Signed(delivery), TestContext.Current.CancellationToken));
            Assert.Equal(0, source.Reads);
        }
        finally
        {
            await first.DisposeAsync();
        }

        await using var restored = await GitHubScenario.StartAsync(runWorker: true, journal: journal, restoredBinding: binding, restoredSource: source);
        using var actor = VerifiedActor.Enter(restored.Actor);
        await GitHubScenario.EventuallyAsync(async () => (await restored.Repository.RequestAsync(new ReadPullRequest(1), TestContext.Current.CancellationToken)).Snapshot is not null);
        Assert.Equal(WebhookAcceptance.Duplicate, await restored.Handler.HandleAsync(restored.Signed(delivery), TestContext.Current.CancellationToken));
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task Revocation_is_persisted_before_receipt_acknowledges_and_blocks_new_work()
    {
        await using var scenario = await GitHubScenario.StartAsync(configuredModule: true);
        Assert.True(scenario.RegisteredWebhookSurfaces >= 2);
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString(), eventName: "installation", action: "deleted"), TestContext.Current.CancellationToken));
        Assert.False(scenario.Binding.Enabled);
        Assert.Equal(WebhookAcceptance.Unavailable, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repository_refuses_same_owner_subscriptions_from_another_principal()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        var foreign = new ActorContext(PrincipalId.New(), "other-principal");
        var subscriber = scenario.Simulation.Brain.Get<IGitHubTestSubscriber>(PrincipalPartition.InstanceName(foreign.PrincipalId, "review"));
        using (VerifiedActor.Enter(foreign))
        {
            await Assert.ThrowsAsync<NeuronAuthorizationException>(() => subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken));
        }

        using (VerifiedActor.Enter(scenario.Actor))
        {
            await Assert.ThrowsAsync<NeuronAuthorizationException>(() => subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("sha1=abc")]
    [InlineData("sha256=not-hex")]
    [InlineData(null)]
    public void Signature_validation_rejects_invalid_header_formats(string? signature) => Assert.False(GitHubWebhookHandler.ValidateSignature([1, 2, 3], signature, "test-secret-at-least-16"));
}

internal sealed class GitHubScenario : IAsyncDisposable
{
    private GitHubScenario(BrainSimulation simulation, GitHubRepositoryBinding binding, GitHubFakeSource source, GitHubSubscriberGate gate, int surfaces, GitHubRepositoryBindings registry)
    {
        Simulation = simulation;
        Binding = binding;
        Source = source;
        Gate = gate;
        RegisteredWebhookSurfaces = surfaces;
        Actor = new(binding.Principal, "github-owner");
        Handler = new(binding, simulation.Grains);
        Registry = registry;
        Processor = new(registry, source, simulation.Grains);
    }

    internal BrainSimulation Simulation { get; }
    internal GitHubRepositoryBinding Binding { get; }
    internal GitHubFakeSource Source { get; }
    internal GitHubSubscriberGate Gate { get; }
    internal ActorContext Actor { get; }
    internal GitHubWebhookHandler Handler { get; }
    internal GitHubWebhookProcessor Processor { get; }
    internal GitHubRepositoryBindings Registry { get; }
    internal int RegisteredWebhookSurfaces { get; }
    internal NeuronReference<IRepository> Repository => Simulation.Brain.Get<IRepository>(Binding.InstanceName);
    internal IAuthenticatedWebhookIngress Ingress => Simulation.Grains.GetGrain<IAuthenticatedWebhookIngress>(Repository.Id.ToGrainId());

    internal INeuronQuery Query(NeuronId id) => Simulation.Grains.GetGrain<INeuronQuery>(id.ToGrainId());
    internal NeuronReference<IGitHubTestSubscriber> Subscriber(string name) => Simulation.Brain.Get<IGitHubTestSubscriber>(PrincipalPartition.InstanceName(Actor.PrincipalId, name));
    internal Task<GitHubReceived> ReadAsync(NeuronReference<IGitHubTestSubscriber> subscriber) => subscriber.RequestAsync(new ReadGitHubReceived(), TestContext.Current.CancellationToken);
    internal static async Task<GitHubScenario> StartAsync(bool configuredModule = false, bool runWorker = false, IJournalStorageProvider? journal = null, GitHubRepositoryBinding? restoredBinding = null, GitHubFakeSource? restoredSource = null)
    {
        var binding = restoredBinding ?? new GitHubRepositoryBinding("fixture", new OwnerId(DigitalBrainNames.DefaultOwner), PrincipalId.New(), 42, 43, 44, "owner", "repository", "fixture-private-key", "fixture-webhook-secret");
        var source = restoredSource ?? new GitHubFakeSource();
        var registry = new GitHubRepositoryBindings([binding]);
        var gate = new GitHubSubscriberGate();
        var configuration = new Dictionary<string, string?>
        {
            [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode
        };
        if (configuredModule)
        {
            var root = GitHubRepositoryBindings.ConfigurationRoot + ":fixture:";
            foreach (var entry in new Dictionary<string, string>
            {
                ["Owner"] = binding.Owner.Value,
                ["Principal"] = binding.Principal.Value.ToString(),
                ["RepositoryId"] = "42",
                ["InstallationId"] = "43",
                ["AppId"] = "44",
                ["RepoOwner"] = "owner",
                ["RepoName"] = "repository",
                ["PrivateKeyPem"] = "fixture-private-key",
                ["WebhookSecret"] = "fixture-webhook-secret",
            }

            )
            {
                configuration[root + entry.Key] = entry.Value;
            }
        }

        var surfaces = 0;
        var simulation = await BrainSimulation.StartAsync(new() { Modules = new ModuleManifest([typeof(DigitalBrain.Execution.ExecutionModule), typeof(DigitalBrain.UI.UIModule), typeof(AIModule), typeof(MicrosoftModule)]), Configuration = configuration, ConfigureSilo = silo =>
        {
            surfaces = silo.Services.Count(item => item.ServiceType == typeof(IHttpSurface));
            foreach (var registration in silo.Services.Where(item => !runWorker && item.ImplementationType?.Name == "WebhookWorker").ToArray())
            {
                silo.Services.Remove(registration);
            }

            if (configuredModule)
            {
                registry = (GitHubRepositoryBindings)silo.Services.Last(item => item.ServiceType == typeof(GitHubRepositoryBindings)).ImplementationInstance!;
                binding = registry.Find("fixture")!;
            }
            else
            {
                silo.Services.AddSingleton(registry);
            }

            silo.Services.AddSingleton<IGitHubRepositorySource>(source);
            silo.Services.AddSingleton(gate);
            if (journal is not null)
            {
                silo.Services.AddSingleton(journal);
            }
        }, });
        return new(simulation, binding, source, gate, surfaces, registry);
    }

    internal Task<WebhookAcceptance> AcceptAsync(string delivery) => Ingress.AcceptAsync(new(delivery, GitHubRepositorySource.Hash(delivery), Binding.Revision, new RefreshRepository(Binding.Id, delivery, Binding.Revision, 1), DateTimeOffset.UtcNow));
    internal async Task DrainAsync()
    {
        for (var index = 0; index < 100; index++)
        {
            var work = await Ingress.ClaimAsync();
            if (work is null)
            {
                return;
            }

            if (work.FenceEpoch is { } epoch)
            {
                await Simulation.Grains.GetGrain<INeuronGrain>(work.Recipient!.Value.ToGrainId()).FenceSourceEpoch(Repository.Id, epoch);
                await Ingress.AcknowledgeAsync(work.Lease, true);
            }
            else if (work.Receipt is { } receipt)
            {
                using var trace = WebhookTrace.Start("webhook.process", receipt);
                await Ingress.CompleteAsync(work.Lease, await Processor.ProcessAsync(Repository.Id, receipt, TestContext.Current.CancellationToken));
            }
            else
            {
                var result = await Simulation.Grains.GetGrain<INeuronGrain>(work.Recipient!.Value.ToGrainId()).Deliver(work.Delivery!, TestContext.Current.CancellationToken);
                await Ingress.AcknowledgeAsync(work.Lease, result == DeliveryOutcome.Handled || work.IsReply);
            }
        }

        throw new InvalidOperationException("The test source did not quiesce.");
    }

    internal WebhookRequest Signed(string delivery, int number = 1, long repositoryId = 42, string eventName = "pull_request", string action = "opened")
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { action, number, hook = new { app_id = Binding.AppId }, installation = new { id = 43 }, repository = new { id = repositoryId, name = "repository", owner = new { login = "owner" } }, });
        return new(body, new Dictionary<string, string[]> { ["X-GitHub-Delivery"] = [delivery], ["X-GitHub-Event"] = [eventName], ["X-Hub-Signature-256"] = ["sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Binding.WebhookSecret), body))], });
    }

    internal static async Task EventuallyAsync(Func<Task<bool>> predicate, int seconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (!await predicate())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The durable GitHub delivery did not finish within its test budget.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    public ValueTask DisposeAsync() => Simulation.DisposeAsync();
}

internal sealed class GitHubFakeSource : IGitHubRepositorySource
{
    internal PullRequestSnapshot Snapshot { get; set; } = new(1, "Example PR", "https://github.com/owner/repository/pull/1", true, false, new string ('a', 40), new string ('b', 40), null, new string ('a', 40), [], true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "revision", "pending", 42);

    internal int Reads;
    internal bool Block;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepositoryBinding binding, int number, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Reads);
        binding.Authorize(binding.Owner, binding.Principal);
        Entered.TrySetResult();
        if (Block)
        {
            await Release.Task.WaitAsync(cancellationToken);
        }

        return Snapshot;
    }

    public async Task<IReadOnlyList<PullRequestSnapshot>> ListOpenPullRequestsAsync(GitHubRepositoryBinding binding, CancellationToken cancellationToken) => [await GetPullRequestAsync(binding, Snapshot.Number, cancellationToken)];
    public Task<GitHubReviewEvidence> GetReviewEvidenceAsync(GitHubRepositoryBinding binding, PullRequestSnapshot snapshot, CancellationToken cancellationToken) => Task.FromResult(new GitHubReviewEvidence(snapshot.HeadSha, snapshot.BaseSha, "bounded evidence", "evidence-hash", true));
}

internal sealed class GitHubSubscriberGate
{
    internal NeuronId? Blocked;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[Alias("github-test-subscriber")]
public interface IGitHubTestSubscriber : INeuron, IHandle<PullRequestChanged>, IHandle<RepositoryAccessRevoked>, IHandle<ReadGitHubReceived>;
[GenerateSerializer, Alias("github-test.read")]
public sealed record ReadGitHubReceived : Signal<GitHubReceived>;
[GenerateSerializer, Alias("github-test.received")]
public sealed record GitHubReceived([property: Id(0)] int Changed, [property: Id(1)] int Revoked = 0) : Signal;
[GrainType("githubtestsubscriber")]
internal sealed class GitHubTestSubscriber(NeuronRuntime runtime, GitHubSubscriberGate gate) : Neuron(runtime), IGitHubTestSubscriber
{
    private readonly HashSet<string> _received = [];
    private int _revoked;
    public async Task HandleAsync(PullRequestChanged signal, CancellationToken cancellationToken)
    {
        if (gate.Blocked == Id)
        {
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(cancellationToken);
        }

        _received.Add(signal.EventId);
    }

    public Task HandleAsync(RepositoryAccessRevoked signal, CancellationToken cancellationToken)
    {
        _revoked++;
        return Task.CompletedTask;
    }

    public Task HandleAsync(ReadGitHubReceived signal, CancellationToken cancellationToken) => ReplyAsync(new GitHubReceived(_received.Count, _revoked));
}
