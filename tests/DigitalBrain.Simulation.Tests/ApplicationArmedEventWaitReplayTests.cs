using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationArmedEventWaitReplayTests
{
    [Fact(Timeout = 30000)]
    public async Task Replay_cannot_change_state_written_between_arming_and_resolving_a_wait()
    {
        var clock = new AdvanceableClock();
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "armed-wait-replay", actor);
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        var application = brain.Application("surface-command");
        var behavior = application.Behavior("surface");
        var retained = behavior.State<string>("retained", 1);
        var reachedResolvedWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var open = behavior.Command<string, string>("open", async (componentKey, run, token) =>
        {
            var response = await run.BeginWaitAsync(renderer.Events().ComponentAdded, token);
            var component = new SurfaceComponent("button", componentKey,
                new Dictionary<string, string> { ["intent"] = "save" });
            await run.SendAsync(renderer,
                new OpenSurface(new CommandId(run.OperationId), "home", "Home", component), token);
            run.State.Set(retained, Interlocked.Increment(ref attempts) == 1 ? "first" : "changed-on-replay");
            var result = await response.ResultAsync(token);
            reachedResolvedWait.TrySetResult();
            await resume.Task.WaitAsync(token);
            return result.Component.Key ?? "";
        });
        using (VerifiedActor.Enter(actor))
        {
            await application.InstallAsync("r1", ct);
        }
        var invocation = await open.SubmitAsync("save-button", Guid.NewGuid(), ct);
        using (var firstStopping = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var firstWorker = simulation.ServeApplicationAsync(application, "r1", firstStopping.Token);
            await reachedResolvedWait.Task.WaitAsync(ct);
            await firstStopping.CancelAsync();
            await firstWorker;
        }

        clock.Advance(TimeSpan.FromSeconds(31));
        resume.TrySetResult();
        using var recoveredStopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var recoveredWorker = simulation.ServeApplicationAsync(application, "r1", recoveredStopping.Token);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                invocation.ResultAsync(recoveredStopping.Token));
            Assert.Contains("Determinism violation", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await recoveredStopping.CancelAsync();
            await recoveredWorker;
        }
    }

    private sealed class AdvanceableClock : TimeProvider
    {
        private long ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref ticks));
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }
}
