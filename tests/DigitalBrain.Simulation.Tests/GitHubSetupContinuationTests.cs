using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Product.Identity;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubSetupContinuationTests
{
    private static readonly OwnerId Owner = new("test");
    private static readonly ActorContext Actor = new(new PrincipalId(Guid.NewGuid()), "owner");
    private const string Url = "https://github.com/intochat/digitalbrain";

    [Fact]
    public async Task Login_preserves_exact_behavior_revision_and_resume_ignores_changed_model_arguments()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var requests = new Requests();
        var setup = new Setup { State = "authentication_required" };
        var logins = Logins(services);
        var context = Turn();
        using var actor = VerifiedActor.Enter(Actor);
        using var turn = AgentTurnContext.Enter(context);
        using var tools = new AgentToolContext(new("assistant", Owner, "assistant"), Actor.PrincipalId, requests);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(await new GitHubSetupTools(setup, logins).GetToolsAsync(tools, CancellationToken.None)));
        await function.InvokeAsync(new AIFunctionArguments { ["repositoryUrl"] = Url, ["behaviorName"] = "review" }, TestContext.Current.CancellationToken);
        var action = Assert.IsType<UserActionRequest>(logins.Find(Owner, context.CommandId));
        Assert.Equal(new("connect_github_repository", Url, "review", requests.Revision), action.SetupContinuation);
        Assert.Empty(requests.Writes);

        // A restarted provider remints only the browser capability; the scope/revision stay exact.
        var recovered = Assert.IsType<UserActionRequest>(Logins(services).Recover(context, action));
        Assert.Equal(action.SetupContinuation, recovered.SetupContinuation);
        Assert.NotEqual(action.LoginUrl, recovered.LoginUrl);
        setup.State = "ready";
        using var resumed = AgentTurnContext.Enter(context with
        {
            AllowedToolNames = ["connect_github_repository"], SetupContinuation = recovered.SetupContinuation,
        });
        await function.InvokeAsync(new AIFunctionArguments { ["repositoryUrl"] = "https://github.com/other/other", ["behaviorName"] = "unrelated" }, TestContext.Current.CancellationToken);
        Assert.Equal(Url, setup.LastUrl);
        Assert.All(requests.Writes.Where(write => write.Target.Type == "behavior"), write => Assert.Equal(PrincipalPartition.InstanceName(Actor.PrincipalId, "review"), write.Target.Name));
        var enabled = Assert.IsType<EnableBehavior>(requests.Writes.Last().Signal);
        Assert.Equal(requests.Revision, enabled.ExpectedDraftRevision);
        Assert.Equal(3, requests.Writes.Count);
    }

    [Fact]
    public async Task Editing_a_draft_during_login_refuses_all_setup_writes()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var requests = new Requests();
        var context = Turn() with { AllowedToolNames = ["connect_github_repository"],
            SetupContinuation = new("connect_github_repository", Url, "review", Guid.NewGuid()) };
        using var actor = VerifiedActor.Enter(Actor);
        using var turn = AgentTurnContext.Enter(context);
        using var tools = new AgentToolContext(new("assistant", Owner, "assistant"), Actor.PrincipalId, requests);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(await new GitHubSetupTools(new Setup(), Logins(services)).GetToolsAsync(tools, CancellationToken.None)));
        await Assert.ThrowsAsync<McpOperationException>(async () => await function.InvokeAsync(new AIFunctionArguments { ["repositoryUrl"] = Url, ["behaviorName"] = "review" }, TestContext.Current.CancellationToken));
        Assert.Empty(requests.Writes);
    }

    private static AgentTurnContext Turn() => new(new("chat", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "main")), CommandId.New(), Actor);
    private static GitHubLogins Logins(IServiceProvider services) => new(new GitHubOAuthConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["DigitalBrain:Microsoft:GitHub:App:AppId"] = "1",
            ["DigitalBrain:Microsoft:GitHub:App:ClientId"] = "test",
            ["DigitalBrain:Microsoft:GitHub:App:ClientSecret"] = "test",
            ["DigitalBrain:Microsoft:GitHub:App:PublicOrigin"] = "http://localhost:5080",
        }).Build()), services);

    private sealed class Setup : IGitHubSetup
    {
        public string State { get; set; } = "ready";
        public string? LastUrl { get; private set; }
        public Task<GitHubSetupResult> ResolveAsync(OwnerId owner, PrincipalId principal, string repositoryUrl, CancellationToken cancellationToken = default)
        {
            LastUrl = repositoryUrl;
            return Task.FromResult(new GitHubSetupResult(Url, State, NeuronId.For<IRepository>(owner, PrincipalPartition.InstanceName(principal, "repository")), [], "fixture"));
        }
        public Task<GitHubSetupResult> ConnectAsync(OwnerId owner, PrincipalId principal, GitHubRepositoryAccess access, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class Requests : IAgentRequests
    {
        public Guid Revision { get; } = Guid.NewGuid();
        public List<(NeuronId Target, Signal Signal)> Writes { get; } = [];
        public Task<DeliveryOutcome> SendAsync(NeuronId target, Signal signal, CancellationToken cancellationToken = default)
        {
            Writes.Add((target, signal));
            return Task.FromResult(DeliveryOutcome.Handled);
        }
        public Task<TResponse> RequestAsync<TResponse>(NeuronId target, Signal<TResponse> request, CancellationToken cancellationToken = default) where TResponse : Signal
        {
            if (request is not ReadBehavior)
            {
                Writes.Add((target, request));
            }
            var program = new BehaviorProgram(Revision, "return Input;", ["PullRequestChanged"], ["Note"], BehaviorValidation.Valid, [], DateTimeOffset.UtcNow);
            return Task.FromResult((TResponse)(Signal)new BehaviorRead(new(target, Actor.PrincipalId, program, program, false, 0, 0, null)));
        }
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request, CancellationToken cancellationToken = default) where TAgent : IAgent
            => throw new NotSupportedException();
    }
}
