using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationProgrammingTests
{
    [Fact]
    public async Task Overlapping_chat_rules_are_rejected_before_they_can_claim_a_message()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "ambiguous-chat",
            new ActorContext(PrincipalId.New(), "author"));
        var app = brain.Application("routing");
        app.OnUserMessageContaining("brainstorm", "brainstorm", (_, _, _) => Task.FromResult("first"));
        app.OnUserMessageContaining("idea", "idea", (_, _, _) => Task.FromResult("second"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.InstallAsync("ambiguous", TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 30000)]
    public async Task Retried_submission_joins_the_original_operation()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var client = DigitalBrainClient.Connect(simulation.Grains, "retry",
            new ActorContext(PrincipalId.New(), "author"));
        var application = client.Application("greeting");
        var executions = 0;
        var reply = application.Command<string, string>("reply", (_, _, _) =>
            Task.FromResult($"reply-{Interlocked.Increment(ref executions)}"));
        await application.InstallAsync("retry-revision", TestContext.Current.CancellationToken);
        var operation = Guid.NewGuid();
        var first = await reply.SubmitAsync("/ping", operation, TestContext.Current.CancellationToken);
        var retry = await reply.SubmitAsync("/ping", operation, TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(application, "retry-revision", stopping.Token);
        try
        {
            Assert.Equal("reply-1", await first.ResultAsync(TestContext.Current.CancellationToken));
            Assert.Equal("reply-1", await retry.ResultAsync(TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reply.SubmitAsync("different input", operation, TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrent_operation_handlers_can_overlap()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var client = DigitalBrainClient.Connect(simulation.Grains, "concurrent",
            new ActorContext(PrincipalId.New(), "author"));
        var application = client.Application("greeting");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = application.Command<string, string>("reply", async (message, _, ct) =>
        {
            if (message == "first")
            {
                firstEntered.SetResult();
                await secondEntered.Task.WaitAsync(ct);
            }
            else
            {
                await firstEntered.Task.WaitAsync(ct);
                secondEntered.SetResult();
            }
            return message;
        });
        await application.InstallAsync("concurrent-revision", TestContext.Current.CancellationToken);
        var first = await reply.SubmitAsync("first", TestContext.Current.CancellationToken);
        var second = await reply.SubmitAsync("second", TestContext.Current.CancellationToken);
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var serving = simulation.ServeApplicationAsync(application, "concurrent-revision", stopping.Token);
        try
        {
            Assert.Equal("second", await second.ResultAsync(stopping.Token));
            Assert.Equal("first", await first.ResultAsync(stopping.Token));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact]
    public async Task Aliased_custom_type_without_a_registered_codec_is_rejected()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var client = DigitalBrainClient.Connect(simulation.Grains, "codec",
            new ActorContext(PrincipalId.New(), "author"));
        var application = client.Application("greeting");

        var error = Assert.Throws<InvalidOperationException>(() =>
            application.Command<UnregisteredRequest, string>("reply", (_, _, _) => Task.FromResult("ignored")));

        Assert.Contains("registered application codec", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Captured_connection_actor_cannot_override_a_foreign_ambient_actor()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var client = DigitalBrainClient.Connect(simulation.Grains, "actor-scope",
            new ActorContext(PrincipalId.New(), "author"));
        var application = client.Application("greeting");
        application.Command<string, string>("reply", (message, _, _) => Task.FromResult(message));

        using var foreign = VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "foreign"));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
            application.InstallAsync("revision-one", TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 30000)]
    public async Task Installed_operation_runs_on_a_later_worker_without_reinstalling()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var client = DigitalBrainClient.Connect(simulation.Grains, "programming", actor);
        var application = client.Application("greeting");
        var reply = application.Command<string, string>("reply", (message, _, _) =>
            Task.FromResult(message == "/ping" ? "pong" : "ignored"));
        await application.InstallAsync("revision-one", TestContext.Current.CancellationToken);

        // No handler is running when the input is admitted.
        var input = await reply.SubmitAsync("/ping", TestContext.Current.CancellationToken);

        await using var laterClient = DigitalBrainClient.Connect(simulation.Grains, "programming", actor);
        var savedReply = laterClient.Application("greeting").Command<string, string>("reply");
        var worker = laterClient.Application("greeting");
        worker.Command<string, string>("reply", (message, _, _) =>
            Task.FromResult(message == "/ping" ? "pong" : "ignored"));
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(worker, "revision-one", stopping.Token);
        try
        {
            Assert.Equal("pong", await input.ResultAsync(TestContext.Current.CancellationToken));
            Assert.Equal("ignored", await savedReply.InvokeAsync("hello", TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }
}

[Alias("tests.unregistered-application-request")]
public sealed record UnregisteredRequest(string Value);

