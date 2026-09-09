using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationNeuronConnectionTests
{
    [Fact(Timeout = 30000)]
    public async Task Application_connects_a_typed_neuron_event_directly_to_an_existing_neuron_input()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "direct-neuron-connection", actor);
        var execution = brain.Get<IActivitySource>(IActivitySource.DefaultInstanceName);
        var activities = brain.Get<IActivities>(IActivities.DefaultInstanceName);
        var connector = brain.Application("activity-wiring");
        connector.Connect("execution-to-activities",
            execution.Events().ExecutionChanged,
            activities.Inputs().ExecutionChanged);

        using (VerifiedActor.Enter(actor))
        {
            await connector.InstallAsync("r1", cancellationToken);
        }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serving = simulation.ServeApplicationAsync(connector, "r1", stopping.Token);
        try
        {
            var correlation = CorrelationId.New();
            await execution.SendAsync(new ActivityExecutionChanged(
                correlation, actor.PrincipalId, "operation", SignalId.New(), null,
                brain.Root.Id, null, "test", "running", DateTimeOffset.UtcNow), cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (true)
            {
                var snapshot = await activities.RequestAsync(new ReadActivities(), timeout.Token);
                if (snapshot.Activities.Any(item => item.CorrelationId == correlation.ToString())) { break; }
                await Task.Delay(25, timeout.Token);
            }
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }
}
