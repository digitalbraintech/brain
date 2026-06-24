using DigitalBrain.Protocol;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DigitalBrain.Silo;

// Typed Roslyn neuron. Wraps the real MSBuildWorkspace solution analysis previously in RoslynArchitectNeuron
// (reuse-first), behind IRoslynNeuron with static-virtual metadata.
[GrainType("digitalbrain.sdk.roslyn.v1")]
public class RoslynNeuron : Neuron, IRoslynNeuron
{
    public RoslynNeuron(ILogger<RoslynNeuron> logger) : base(logger) { }

    public async Task<string> AnalyzeSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
        var projectCount = solution.Projects.Count();

        var diagnostics = new List<string>();
        foreach (var project in solution.Projects.Take(5))
        {
            var compilation = await project.GetCompilationAsync(ct);
            var errors = compilation!.GetDiagnostics(ct)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3);
            diagnostics.AddRange(errors.Select(e => $"{project.Name}:{e.Location} {e.GetMessage()}"));
        }

        var report = $"Solution {solutionPath}: {projectCount} projects. Sample issues: {string.Join("; ", diagnostics)}";
        await FireAsync(new ArchitectReport(solutionPath, report));
        return report;
    }
}
