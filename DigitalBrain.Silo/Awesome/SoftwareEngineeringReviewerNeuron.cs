using DigitalBrain.Protocol;

namespace DigitalBrain.Silo;

// The "awesome" software-engineering experience: real review work via ProjectReview, replacing the old
// string-templating teams. Handles a content review (ReviewRequest) and a kernel-local project review
// (ReviewProjectRequest), emitting a typed ReviewResult.
[GrainType("awesome.se.reviewer.v1")]
public class SoftwareEngineeringReviewerNeuron : Neuron, ISoftwareEngineeringReviewer
{
    public SoftwareEngineeringReviewerNeuron(ILogger<SoftwareEngineeringReviewerNeuron> logger) : base(logger) { }

    public async Task HandleAsync(ReviewProjectRequest request)
    {
        var review = ProjectReview.Analyze(request.Path);
        await FireAsync(new ReviewResult(request.Path, review.Summary, review.Report, review.FileCount, review.TodoCount, review.Truncated));
        await FireAsync(new NeuronTelemetry(Self, "ProjectReviewed", review.FileCount));
    }

    public async Task HandleAsync(ReviewRequest request)
    {
        var issues = request.DiffOrContent.Contains("TODO", StringComparison.Ordinal) ? 1 : 0;
        var summary = $"Reviewed {request.Target}. Length={request.DiffOrContent.Length}. Issues found: {issues}. " +
                      (issues > 0 ? "Suggestion: address TODOs." : "Suggestion: looks good, consider adding tests.");
        await FireAsync(new ReviewResult(request.Target, summary, summary, 0, issues, false));
    }
}
