using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationSequentialDelayTests
{
    [Fact(Timeout = 30000)]
    public async Task Intentional_suspensions_do_not_consume_worker_crash_recovery_attempts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new AdvanceableClock(DateTimeOffset.UtcNow);
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "sequential-delays",
            new ActorContext(PrincipalId.New(), "author"));
        var lastReachedStep = 0;
        var application = brain.Application("delays");
        var command = application.Command<string, string>("wait", async (_, run, ct) =>
        {
            for (var step = 1; step <= 4; step++)
            {
                await run.DelayAsync(TimeSpan.FromMinutes(1), ct);
                Volatile.Write(ref lastReachedStep, step);
            }
            return "done";
        });
        await application.InstallAsync("r1", cancellationToken);
        var invocation = await command.SubmitAsync("start", Guid.NewGuid(), cancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(application, "r1", stopping.Token);
        try
        {
            for (var step = 1; step <= 4; step++)
            {
                await WaitUntilAsync(async () =>
                    (await application.ReadInvocationAsync(invocation.OperationId, cancellationToken)).Status == "waiting",
                    cancellationToken);
                clock.Advance(TimeSpan.FromMinutes(1));
                await WaitForStepAsync(application, invocation, () => Volatile.Read(ref lastReachedStep),
                    step, cancellationToken);
            }
            Assert.Equal("done", await invocation.ResultAsync(cancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForStepAsync(ApplicationDefinition application,
        ApplicationInvocation<string> invocation, Func<int> readStep, int expected,
        CancellationToken cancellationToken)
    {
        while (readStep() < expected)
        {
            var status = await application.ReadInvocationAsync(invocation.OperationId, cancellationToken);
            if (status.Status == "failed")
            {
                _ = await invocation.ResultAsync(cancellationToken);
            }
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class AdvanceableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }
}
