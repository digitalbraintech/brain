using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationDelayRecoveryTests : IDisposable
{
    private readonly string persistence = Path.Combine(
        Path.GetTempPath(), "db-delay-recovery", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 30000)]
    public async Task Durable_delay_resumes_after_worker_and_silo_restart_at_its_recorded_due_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new AdvanceableClock(DateTimeOffset.UtcNow);
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = persistence,
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "delay-owner", actor);
        var application = brain.Application("delay-app");
        var command = application.Command<string, string>("wait", async (_, run, ct) =>
        {
            await run.DelayAsync(TimeSpan.FromMinutes(5), ct);
            return "elapsed";
        });
        await application.InstallAsync("delay-r1", cancellationToken);
        var invocation = await command.SubmitAsync("start", Guid.NewGuid(), cancellationToken);
        using (var firstStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var firstWorker = simulation.ServeApplicationAsync(application, "delay-r1", firstStopping.Token);
            await WaitUntilAsync(async () =>
                (await application.ReadInvocationAsync(invocation.OperationId, cancellationToken)).Status == "waiting",
                cancellationToken);
            await firstStopping.CancelAsync();
            await firstWorker;
        }

        await simulation.RestartSiloAsync(cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(6));
        await using var recoveredBrain = DigitalBrainClient.Connect(simulation.Grains, "delay-owner", actor);
        var recovered = recoveredBrain.Application("delay-app");
        var recoveredCommand = recovered.Command<string, string>("wait", async (_, run, ct) =>
        {
            await run.DelayAsync(TimeSpan.FromMinutes(5), ct);
            return "elapsed";
        });
        using var recoveredStopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var recoveredWorker = simulation.ServeApplicationAsync(recovered, "delay-r1", recoveredStopping.Token);
        try
        {
            var recoveredInvocation = await recoveredCommand.SubmitAsync(
                "start", invocation.OperationId, cancellationToken);
            Assert.Equal("elapsed", await recoveredInvocation.ResultAsync(cancellationToken));
        }
        finally
        {
            await recoveredStopping.CancelAsync();
            await recoveredWorker;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(persistence)) { Directory.Delete(persistence, recursive: true); }
    }

    private sealed class AdvanceableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }
}
