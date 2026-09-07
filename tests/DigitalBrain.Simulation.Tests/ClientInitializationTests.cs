using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ClientInitializationTests
{
    [Fact(Timeout = 30000)]
    public async Task Initialization_identity_and_journal_survive_silo_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-initialization", Guid.NewGuid().ToString("N"));
        try
        {
            await using var simulation = await BrainSimulation.StartAsync(new()
            {
                Modules = new([]), PersistenceDirectory = directory,
            });
            await simulation.Brain.ActivateAsync(TestContext.Current.CancellationToken);
            var initial = await simulation.Brain.ReadJournalAsync(JournalKind.Outgoing,
                cancellationToken: TestContext.Current.CancellationToken);
            var identity = Assert.Single(initial.Delta, entry => entry.Signal is DigitalBrainActivated).SignalId;

            await simulation.RestartSiloAsync(TestContext.Current.CancellationToken);
            await simulation.Brain.ActivateAsync(TestContext.Current.CancellationToken);
            var recovered = await simulation.Brain.ReadJournalAsync(JournalKind.Outgoing,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(identity, Assert.Single(recovered.Delta, entry => entry.Signal is DigitalBrainActivated).SignalId);
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Ordinary_operations_do_not_initialize_the_brain()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var brain = simulation.Brain;
        await brain.Get<IActivities>(IActivities.DefaultInstanceName).RequestAsync(
            new ReadActivities(), TestContext.Current.CancellationToken);

        var before = await brain.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(before.Delta, entry => entry.Signal is DigitalBrainActivated);

        await brain.ActivateAsync(TestContext.Current.CancellationToken);
        await brain.ActivateAsync(TestContext.Current.CancellationToken);
        var after = await brain.ReadJournalAsync(JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(after.Delta, entry => entry.Signal is DigitalBrainActivated);
    }
}

