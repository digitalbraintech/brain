using DigitalBrain.Core;
using DigitalBrain.Context;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

[GrainType("context.manager.v1")]
public class ContextNeuron : Neuron, IContextNeuron, IHandle<Signal>
{
    public ContextNeuron(ILogger<ContextNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(ContextUpdate cmd)
    {
        Logger.LogInformation("Context updated: {Name}.{Key} = {Val}", cmd.ContextName, cmd.Key, cmd.Value);
        await FireAsync(cmd);
    }

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != ContextSignals.RecallRequested) return;
        var query = signal.Props.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
        var results = await RecallAsync(query);
        var replyProps = new Dictionary<string, object?>(signal.Props) { ["results"] = results };
        replyProps.Remove("query");
        await FireAsync(new Signal(ContextSignals.RecallCompleted, replyProps));
    }

    public Task<string> GetContextAsync(string contextName)
    {
        var entries = OutgoingJournal.Concat(IncomingJournal).OfType<ContextUpdate>()
            .Where(c => c.ContextName == contextName)
            .Take(10)
            .Select(c => $"{c.Key}={c.Value}");
        return Task.FromResult(string.Join("; ", entries));
    }

    public async Task RememberAsync(string text)
    {
        var embedding = await EmbedAsync(text);
        await FireAsync(new MemoryStored(text, embedding));
    }

    public async Task<string[]> RecallAsync(string query, int top = 5)
    {
        var queryEmbedding = await EmbedAsync(query);
        var memories = OutgoingJournal.Concat(IncomingJournal).OfType<MemoryStored>();
        return memories
            .Select(m => (m.Text, Score: HybridScorer.Score(query, m.Text, queryEmbedding, m.Embedding)))
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .Take(top)
            .Select(x => x.Text)
            .ToArray();
    }

    private async Task<float[]> EmbedAsync(string text)
    {
        var generator = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (generator is null) return [];
        var generated = await generator.GenerateAsync([text]);
        return generated.First().Vector.ToArray();
    }
}


