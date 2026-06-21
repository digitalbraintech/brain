using DigitalBrain.Protocol;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Silo;

public interface IDemoNeuron : INeuron
{
    Task<string> GetLastMessageAsync();
}

public class DemoNeuron : Neuron, IDemoNeuron, IHandle<DemoMessageSynapse>
{
    private string _last = string.Empty;

    public DemoNeuron(ILogger<DemoNeuron> logger, [PersistentState("journal", "Default")] IPersistentState<List<Synapse>> journal)
        : base(logger, journal)
    {
    }

    public async Task HandleAsync(DemoMessageSynapse synapse)
    {
        _last = synapse.Text;
        Logger.LogInformation("Demo received via IHandle: {Text}", synapse.Text);
        await FireAsync(new NeuronTelemetry(Self, "message-handled"));
    }

    public Task<string> GetLastMessageAsync() => Task.FromResult(_last);
}

// DemoMessageSynapse moved to Protocol for CLI/shared use