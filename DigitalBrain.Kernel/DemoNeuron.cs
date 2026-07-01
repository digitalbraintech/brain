using DigitalBrain.Core;
using DigitalBrain.UiKit;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Kernel;

public class DemoNeuron : Neuron, IDemoNeuron, IHandle<DemoMessageSynapse>
{
    public DemoNeuron(ILogger<DemoNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(DemoMessageSynapse synapse)
    {
        Logger.LogInformation("Demo received via IHandle: {Text}", synapse.Text);
        await FireAsync(new NeuronTelemetry(Self, "message-handled"));

        if (synapse.Text != null && synapse.Text.Contains("hello-world", StringComparison.OrdinalIgnoreCase))
        {
            var toast = new UiSurface("toast", new Dictionary<string, object?>
            {
                ["title"] = "Hello World!",
                ["description"] = "Runtime neuron-driven ForUI notification from Software Engineering demo."
            });
            await FireAsync(toast);

            // Prefer routing through IFlutterUiNeuron (item 14 complete for this emitter); flutter neuron owns bridge + broadcast.
            var flutter = GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
            await flutter.DeliverAsync(toast.Stamp(Self, CurrentCause));
        }
    }

    public Task<string> GetLastMessageAsync()
    {
        // Derive from journal (no private state).
        var last = IncomingJournal.OfType<DemoMessageSynapse>().LastOrDefault()
                ?? OutgoingJournal.OfType<DemoMessageSynapse>().LastOrDefault();
        return Task.FromResult(last?.Text ?? "");
    }
}

// DemoMessageSynapse moved to Protocol for CLI/shared use
