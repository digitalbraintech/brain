using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using System.Reflection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection("Application artifact chat")]
public sealed class ApplicationChatIngressTests : IDisposable
{
    private readonly string testRoot = Path.Combine(FindRepositoryRoot(), ".chat-app-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 120000)]
    public async Task Compiled_application_claims_ping_and_publishes_one_correlated_response()
    {
        var artifact = await CompilePingApplicationAsync();
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var client = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);
        var inbox = client.Get<IComposer>(IComposer.DefaultInstanceName);
        var fallback = client.Get<ITestUserMessageFallback>("fallback");
        await fallback.SubscribeToAsync<ITestUserMessageFallback, IComposer, UserMessaged>(inbox.Id,
            TestContext.Current.CancellationToken);
        var host = new ApplicationArtifactHost();
        await host.InstallAsync(artifact, client, "ping", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = host.ServeAsync(artifact, client, "ping", stopping.Token);
        try
        {
            CommandId command;
            using (VerifiedActor.Enter(actor))
            {
                command = await new WorkspaceInject(client, simulation.Grains).InjectUserMessage(
                    "main", "/ping", actor, TestContext.Current.CancellationToken);
            }
            var response = await WaitForResponseAsync(inbox, command);
            var input = await WaitForInputAsync(inbox, command);

            Assert.Equal("pong", Assert.IsType<Responded>(response.Signal).Text);
            Assert.Equal(input.CorrelationId, response.CorrelationId);
            Assert.Equal(actor.PrincipalId, response.Principal);
            Assert.DoesNotContain((await fallback.ReadJournalAsync(JournalKind.Incoming, 0,
                TestContext.Current.CancellationToken)).Delta,
                delivery => delivery.Signal is UserMessaged message && message.CommandId == command);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact(Timeout = 120000)]
    public async Task Unmatched_message_falls_back_without_disabling_the_explicit_route()
    {
        var artifact = await CompilePingApplicationAsync();
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var client = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);
        var inbox = client.Get<IComposer>(IComposer.DefaultInstanceName);
        var fallback = client.Get<ITestUserMessageFallback>("fallback");
        await fallback.SubscribeToAsync<ITestUserMessageFallback, IComposer, UserMessaged>(inbox.Id,
            TestContext.Current.CancellationToken);
        var host = new ApplicationArtifactHost();
        await host.InstallAsync(artifact, client, "ping", TestContext.Current.CancellationToken);

        CommandId command;
        using (VerifiedActor.Enter(actor))
        {
            command = await new WorkspaceInject(client, simulation.Grains).InjectUserMessage(
                "main", "hello", actor, TestContext.Current.CancellationToken);
        }

        await WaitUntilAsync(async () => (await fallback.ReadJournalAsync(JournalKind.Incoming, 0,
            TestContext.Current.CancellationToken)).Delta.Any(delivery =>
                delivery.Signal is UserMessaged message && message.CommandId == command));
    }

    [Fact(Timeout = 120000)]
    public async Task Retried_command_id_does_not_execute_or_publish_twice()
    {
        var artifact = await CompilePingApplicationAsync();
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var client = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);
        var inbox = client.Get<IComposer>(IComposer.DefaultInstanceName);
        var host = new ApplicationArtifactHost();
        await host.InstallAsync(artifact, client, "ping", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = host.ServeAsync(artifact, client, "ping", stopping.Token);
        try
        {
            var command = CommandId.New();
            var inject = new WorkspaceInject(client, simulation.Grains);
            using (VerifiedActor.Enter(actor))
            {
                await inject.InjectUserMessage("main", "/ping", actor, TestContext.Current.CancellationToken, command);
                await inject.InjectUserMessage("main", "/ping", actor, TestContext.Current.CancellationToken, command);
            }
            await WaitForResponseAsync(inbox, command);

            var responses = (await inbox.ReadJournalAsync(JournalKind.Outgoing, 0,
                TestContext.Current.CancellationToken)).Delta
                .Count(delivery => delivery.Signal is Responded response && response.CommandId == command);
            Assert.Equal(1, responses);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact]
    public async Task Workspace_ingress_rejects_an_unverified_actor()
    {
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var client = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
            new WorkspaceInject(client, simulation.Grains).InjectUserMessage(
                "main", "/ping", actor, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Composer_rejects_user_messages_from_a_non_root_source()
    {
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        var inbox = NeuronId.For<IComposer>(new OwnerId(DigitalBrainNames.DefaultOwner), IComposer.DefaultInstanceName);
        var sourceId = NeuronId.For<ITestUserMessageFallback>(inbox.Owner, "source");
        var source = simulation.Grains.GetGrain<ITestUserMessageFallback>(sourceId.ToGrainId());
        var signal = new UserMessaged(CommandId.New(), inbox, "/ping", actor);

        using var verified = VerifiedActor.Enter(actor);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => source.FireAt(inbox, signal));
    }

    [Fact(Timeout = 120000)]
    public async Task Failed_claim_is_visible_on_the_shared_response_journal()
    {
        var artifact = await CompilePingApplicationAsync("Task.FromException<string>(new InvalidOperationException(\"ping failed\"))");
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var client = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);
        var inbox = client.Get<IComposer>(IComposer.DefaultInstanceName);
        var host = new ApplicationArtifactHost();
        await host.InstallAsync(artifact, client, "ping", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = host.ServeAsync(artifact, client, "ping", stopping.Token);
        try
        {
            CommandId command;
            using (VerifiedActor.Enter(actor))
            {
                command = await new WorkspaceInject(client, simulation.Grains).InjectUserMessage(
                    "main", "/ping", actor, TestContext.Current.CancellationToken);
            }

            await WaitUntilAsync(async () => (await inbox.ReadJournalAsync(JournalKind.Outgoing, 0,
                TestContext.Current.CancellationToken)).Delta.Any(delivery =>
                    delivery.Signal is TurnLifecycle lifecycle
                    && lifecycle.CommandId == command
                    && lifecycle.Status == ChatTurnStatus.Failed));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact]
    public void Composer_response_retention_fails_closed_at_its_bound()
    {
        var composer = typeof(UIModule).Assembly.GetType("DigitalBrain.UI.UserMessagesNeuron", throwOnError: true)!;
        var guard = composer.GetMethod("EnsureResponseCapacity", BindingFlags.Static | BindingFlags.NonPublic)!;

        var error = Assert.Throws<TargetInvocationException>(() => guard.Invoke(null, [100_000]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Fact]
    public async Task Same_command_id_from_two_principals_publishes_two_independent_responses()
    {
        await using var simulation = await StartAsync();
        var owner = new OwnerId(DigitalBrainNames.DefaultOwner);
        var inboxId = NeuronId.For<IComposer>(owner, IComposer.DefaultInstanceName);
        var inbox = simulation.Grains.GetGrain<INeuronQuery>(inboxId.ToGrainId());
        var assistantId = new NeuronId("assistant", owner, "assistant");
        var assistant = simulation.Grains.GetGrain<ITestAssistantSource>(assistantId.ToGrainId());
        var command = CommandId.New();

        using (VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "first")))
        {
            await assistant.FireAt(inboxId, new Responded(command, inboxId, "first", "assistant"));
        }
        using (VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "second")))
        {
            await assistant.FireAt(inboxId, new Responded(command, inboxId, "second", "assistant"));
        }

        var responses = (await inbox.ReadJournal(JournalKind.Outgoing, 0)).Delta
            .Count(delivery => delivery.Signal is Responded response && response.CommandId == command);
        Assert.Equal(2, responses);
    }

    [Fact(Timeout = 120000)]
    public async Task Authored_contains_rule_claims_brainstorm_inside_a_message_case_insensitively()
    {
        var artifact = await CompilePingApplicationAsync("Task.FromResult(\"brainstorm accepted\")", contains: true);
        await using var simulation = await StartAsync();
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);
        var inbox = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var host = new ApplicationArtifactHost();
        await host.InstallAsync(artifact, brain, "ping", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = host.ServeAsync(artifact, brain, "ping", stopping.Token);
        try
        {
            CommandId command;
            using (VerifiedActor.Enter(actor))
            {
                command = await new WorkspaceInject(brain, simulation.Grains).InjectUserMessage(
                    "main", "Please BRAINSTORM about my idea", actor, TestContext.Current.CancellationToken);
            }
            var response = await WaitForResponseAsync(inbox, command);
            Assert.Equal("brainstorm accepted", Assert.IsType<Responded>(response.Signal).Text);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    private async Task<FileApplicationArtifact> CompilePingApplicationAsync(
        string handler = "Task.FromResult(\"pong\")", bool contains = false)
    {
        Directory.CreateDirectory(testRoot);
        var source = Path.Combine(testRoot, "ping.cs");
        await File.WriteAllTextAsync(source, """
            #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var app = brain.Application("ping");
            app.OnUserMessage("ping", "/ping", (_, _, _) => HANDLER);
            await app.RunAsync(args);
            """.Replace("HANDLER", handler, StringComparison.Ordinal)
                .Replace("app.OnUserMessage(\"ping\", \"/ping\"", contains
                    ? "app.OnUserMessageContaining(\"ping\", \"brainstorm\""
                    : "app.OnUserMessage(\"ping\", \"/ping\"", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        return await new FileApplicationCompiler().CompileAsync(source, Path.Combine(testRoot, "artifacts"),
            TestContext.Current.CancellationToken);
    }

    private static Task<BrainSimulation> StartAsync() => BrainSimulation.StartAsync(new()
    {
        Modules = new ModuleManifest([typeof(UIModule)]),
        Configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode },
    });

    private static async Task<SignalDelivery> WaitForInputAsync(
        NeuronReference<IComposer> inbox, CommandId command)
    {
        SignalDelivery? found = null;
        await WaitUntilAsync(async () =>
        {
            found = (await inbox.ReadJournalAsync(JournalKind.Incoming, 0, TestContext.Current.CancellationToken)).Delta
                .FirstOrDefault(delivery => delivery.Signal is UserMessaged message && message.CommandId == command);
            return found is not null;
        });
        return found!;
    }

    private static async Task<SignalDelivery> WaitForResponseAsync(
        NeuronReference<IComposer> inbox, CommandId command)
    {
        SignalDelivery? found = null;
        await WaitUntilAsync(async () =>
        {
            found = (await inbox.ReadJournalAsync(JournalKind.Outgoing, 0, TestContext.Current.CancellationToken)).Delta
                .FirstOrDefault(delivery => delivery.Signal is Responded response && response.CommandId == command);
            return found is not null;
        });
        return found!;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition()) { await Task.Delay(25, timeout.Token); }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testRoot)) { Directory.Delete(testRoot, recursive: true); }
        }
        catch (UnauthorizedAccessException)
        {
            // Collectible artifact contexts release copied assemblies asynchronously on Windows.
        }
    }
}

[CollectionDefinition("Application artifact chat", DisableParallelization = true)]
public sealed class ApplicationArtifactChatCollection;

[Alias("tests.application-chat-fallback")]
public interface ITestUserMessageFallback : INeuron, IHandle<UserMessaged>
{
    [Alias(nameof(FireAt))]
    Task FireAt(NeuronId target, UserMessaged signal);
}

[GrainType("testusermessagefallback")]
internal sealed class TestUserMessageFallback(NeuronRuntime runtime) : Neuron(runtime), ITestUserMessageFallback
{
    public Task HandleAsync(UserMessaged signal, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task FireAt(NeuronId target, UserMessaged signal)
        => _ = await SendAsync(target, signal).ConfigureAwait(true);
}

[Alias("tests.application-chat-assistant")]
public interface ITestAssistantSource : INeuron
{
    [Alias(nameof(FireAt))]
    Task FireAt(NeuronId target, Responded signal);
}

[GrainType("assistant")]
internal sealed class TestAssistantSource(NeuronRuntime runtime) : Neuron(runtime), ITestAssistantSource
{
    public async Task FireAt(NeuronId target, Responded signal)
        => _ = await SendAsync(target, signal).ConfigureAwait(true);
}

