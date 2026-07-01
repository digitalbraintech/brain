using DigitalBrain.Core;
using DigitalBrain.Developer;

namespace DigitalBrain.Kernel;

// Typed Roslyn neuron. Delegates MSBuildWorkspace solution analysis to the ino-hosted, Orleans-free
// RoslynAnalysisService; this grain only adds the journal-derived ArchitectReport broadcast.
[GrainType("digitalbrain.sdk.roslyn.v1")]
public class RoslynNeuron(ILogger<RoslynNeuron> logger, NeuronJournals journals, RoslynAnalysisService analysis)
    : Neuron(logger, journals), IRoslynNeuron
{
    public async Task<string> AnalyzeSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        var report = await analysis.AnalyzeSolutionAsync(solutionPath, ct);
        await FireAsync(new ArchitectReport(solutionPath, report));
        return report;
    }
}
