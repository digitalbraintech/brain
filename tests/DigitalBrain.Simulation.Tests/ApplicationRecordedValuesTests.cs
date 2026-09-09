using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationRecordedValuesTests
{
    [Fact(Timeout = 30000)]
    public async Task Time_and_guid_are_reused_when_an_interrupted_handler_resumes()
    {
        var clock = new AdvanceableClock();
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "recorded-values", actor);
        using var verified = VerifiedActor.Enter(actor);
        var recorded = new TaskCompletionSource<(DateTimeOffset Time, Guid Id)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = brain.Application("values");
        var command = Declare(first, recorded, hold);
        await first.InstallAsync("values-v1", TestContext.Current.CancellationToken);
        var invocation = await command.SubmitAsync("first", TestContext.Current.CancellationToken);

        (DateTimeOffset Time, Guid Id) original;
        using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var serving = simulation.ServeApplicationAsync(first, "values-v1", stopping.Token);
            original = await recorded.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.NotEqual(Guid.Empty, original.Id);
            await stopping.CancelAsync();
            await SuppressCancellation(serving);
        }

        clock.Advance(TimeSpan.FromSeconds(31));
        var resumed = brain.Application("values");
        var resumedCommand = Declare(resumed);
        using var resumedStopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var resumedServing = simulation.ServeApplicationAsync(resumed, "values-v1", resumedStopping.Token);
        try
        {
            Assert.Equal(Format(original), await invocation.ResultAsync(TestContext.Current.CancellationToken));
            var next = await resumedCommand.InvokeAsync("second", TestContext.Current.CancellationToken);
            var nextId = Guid.Parse(next[(next.LastIndexOf('|') + 1)..]);
            Assert.NotEqual(Guid.Empty, nextId);
            Assert.NotEqual(original.Id, nextId);
        }
        finally
        {
            await resumedStopping.CancelAsync();
            await SuppressCancellation(resumedServing);
        }
    }

    private static CommandPort<string, string> Declare(
        ApplicationDefinition app,
        TaskCompletionSource<(DateTimeOffset Time, Guid Id)>? recorded = null,
        TaskCompletionSource? hold = null)
        => app.Command<string, string>("capture", async (_, run, ct) =>
        {
            var time = await run.UtcNowAsync(ct);
            var id = await run.NewGuidAsync(ct);
            recorded?.TrySetResult((time, id));
            if (hold is not null) { await hold.Task.WaitAsync(ct); }
            return Format((time, id));
        });

    private static string Format((DateTimeOffset Time, Guid Id) value)
        => $"{value.Time:O}|{value.Id:D}";

    private static async Task SuppressCancellation(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }

    private sealed class AdvanceableClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }
}
