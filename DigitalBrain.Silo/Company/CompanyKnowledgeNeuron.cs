using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Silo.Company;

[GrainType("company.knowledge.v1")]
public sealed class CompanyKnowledgeNeuron : Neuron, ICompanyKnowledgeNeuron
{
    public CompanyKnowledgeNeuron(ILogger<CompanyKnowledgeNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(IngestCompanySource cmd)
    {
        var ingestor = ServiceProvider.GetRequiredService<DocumentIngestor>();
        int chunkCount = await ingestor.IngestAsync(cmd.Collection, cmd.SourceId, cmd.Text);

        // Also remember full source text in journaled memory for Recall (hybrid keyword+vector).
        await FireAsync(new MemoryStored(cmd.Text, []));

        await FireAsync(new CompanySourceIngested(cmd.Collection, cmd.SourceId, chunkCount));
    }
}
