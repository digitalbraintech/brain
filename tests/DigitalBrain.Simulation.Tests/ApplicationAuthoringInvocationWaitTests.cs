using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationAuthoringInvocationWaitTests
{
    [Fact(Timeout = 30000)]
    public async Task Authoring_invoke_waits_through_a_durable_delay_for_the_completed_result()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new AdvanceableClock(DateTimeOffset.UtcNow);
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "authoring-delay",
            new ActorContext(PrincipalId.New(), "author"));
        var application = brain.Application("delayed-reply");
        application.Command<string, string>("reply", async (_, run, ct) =>
        {
            await run.DelayAsync(TimeSpan.FromMinutes(5), ct);
            return "finished";
        });
        await application.InstallAsync("r1", cancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serving = simulation.ServeApplicationAsync(application, "r1", stopping.Token);
        try
        {
            var operationId = Guid.NewGuid();
            await application.Command<string, string>("reply").SubmitAsync("start", operationId, cancellationToken);
            var authoring = new ApplicationAuthoringService(Path.GetTempPath());
            var invoking = authoring.InvokeAsync(brain, "delayed-reply", "reply", "\"start\"",
                operationId, cancellationToken);
            while ((await authoring.ReadInvocationAsync(brain, "delayed-reply", operationId,
                cancellationToken)).Status != "waiting")
            {
                await Task.Delay(10, cancellationToken);
            }
            Assert.False(invoking.IsCompleted);

            clock.Advance(TimeSpan.FromMinutes(6));
            var completed = await invoking;
            Assert.Equal("completed", completed.Status);
            Assert.Equal("\"finished\"", completed.Value);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    private sealed class AdvanceableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }
}
