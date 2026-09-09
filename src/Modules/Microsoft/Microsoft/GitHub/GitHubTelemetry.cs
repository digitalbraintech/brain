using System.Diagnostics;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubTelemetry
{
    internal static readonly ActivitySource Source = new("DigitalBrain.GitHub");

    internal static void TagReceipt(Activity? activity, GitHubRepositoryBinding binding, string deliveryId, int? number)
    {
        activity?.SetTag("github.binding.id", binding.Id)
            .SetTag("github.repository.id", binding.RepositoryId)
            .SetTag("github.delivery.id", deliveryId);
        if (number is { } value)
        {
            activity?.SetTag("github.pull_request.number", value);
        }
    }

    internal static void Failed(Activity? activity, Exception error)
        => activity?.SetTag("error.type", error.GetType().Name).SetStatus(ActivityStatusCode.Error, error.GetType().Name);
}
