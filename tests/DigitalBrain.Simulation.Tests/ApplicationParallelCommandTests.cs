using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationParallelCommandTests
{
    [Fact(Timeout = 30000)]
    public async Task Named_branches_compose_pinned_application_command_results_in_parallel()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "parallel-commands", actor);
        using var verified = VerifiedActor.Enter(actor);
        var app = brain.Application("parallel-commands");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var left = app.Command<int, int>("left", async (value, _, ct) =>
        {
            if (Interlocked.Increment(ref started) == 2) { bothStarted.TrySetResult(); }
            await release.Task.WaitAsync(ct);
            return value + 1;
        });
        var right = app.Command<int, int>("right", async (value, _, ct) =>
        {
            if (Interlocked.Increment(ref started) == 2) { bothStarted.TrySetResult(); }
            await release.Task.WaitAsync(ct);
            return value + 2;
        });
        var sum = app.Command<int, int>("sum", async (value, run, ct) =>
        {
            var values = await Task.WhenAll(
                run.Branch("left").InvokeAsync(left, value, ct),
                run.Branch("right").InvokeAsync(right, value, ct));
            return values[0] + values[1];
        });
        await app.InstallAsync("parallel-commands-v1", TestContext.Current.CancellationToken);
        var invocation = await sum.SubmitAsync(10, Guid.NewGuid(), TestContext.Current.CancellationToken);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = simulation.ServeApplicationAsync(app, "parallel-commands-v1", stopping.Token);
        try
        {
            await bothStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            release.TrySetResult();
            Assert.Equal(23, await invocation.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }
}
