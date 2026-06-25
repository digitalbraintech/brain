using DigitalBrain.Core;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

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
