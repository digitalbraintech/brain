namespace DigitalBrain.Microsoft.GitHub;

/// <summary>The behavior supplies requirements and conclusions; only authoritative complete facts satisfy them.</summary>
public static class GitHubReviewPolicy
{
    public static bool ChecksSucceeded(PullRequestSnapshot snapshot,
        IReadOnlyList<GitHubCheckRequirement> requiredChecks, IReadOnlyList<string> acceptedConclusions)
    {
        if (!snapshot.IsOpen || snapshot.IsDraft || !snapshot.ChecksComplete || requiredChecks.Count is < 1 or > 64
            || acceptedConclusions.Count is < 1 or > 8 || string.IsNullOrWhiteSpace(snapshot.CiSha)
            || acceptedConclusions.Any(value => value is not ("success" or "neutral" or "skipped")))
        {
            return false;
        }
        foreach (var requirement in requiredChecks)
        {
            if (string.IsNullOrWhiteSpace(requirement.Name) || requirement.Kind is not ("check" or "status" or "any"))
            {
                return false;
            }
            var matching = snapshot.Checks.Where(check => check.Name == requirement.Name && (requirement.Kind == "any" || check.Kind == requirement.Kind)
                && (requirement.AppId is null || check.AppId == requirement.AppId) && check.Sha == snapshot.CiSha).ToArray();
            // A requirement without a producer must not accidentally accept one green producer
            // while another producer of the same name is red or pending.
            if (matching.Length == 0) { return false; }
            foreach (var producer in matching.GroupBy(check => check.AppId))
            {
                var latest = producer.OrderByDescending(check => check.UpdatedAt).First();
                if (latest.State != "completed" || latest.Conclusion is null
                    || !acceptedConclusions.Contains(latest.Conclusion, StringComparer.Ordinal))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
