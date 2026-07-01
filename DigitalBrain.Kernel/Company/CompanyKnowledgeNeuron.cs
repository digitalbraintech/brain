using DigitalBrain.Core;
using DigitalBrain.Context;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Company;

[GrainType("company.knowledge.v1")]
public sealed class CompanyKnowledgeNeuron(ILogger<CompanyKnowledgeNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICompanyKnowledgeNeuron
{
    public async Task HandleAsync(IngestCompanySource cmd)
    {
        var ingestor = ServiceProvider.GetRequiredService<DocumentIngestor>();
        int chunkCount = await ingestor.IngestAsync(cmd.Collection, cmd.SourceId, cmd.Text);

        // Also remember full source text in journaled memory for Recall (hybrid keyword+vector).
        await FireAsync(new MemoryStored(cmd.Text, []));

        await FireAsync(new CompanySourceIngested(cmd.Collection, cmd.SourceId, chunkCount));
    }
}
