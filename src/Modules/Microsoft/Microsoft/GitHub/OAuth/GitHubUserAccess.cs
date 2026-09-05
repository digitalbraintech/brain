using System.Net.Http.Headers;
using System.Text.Json;
using DigitalBrain.Sdk;

namespace DigitalBrain.Microsoft.GitHub;

// The user's token proves the intersection of user access and App installation access.
// It exists only during this callback; ongoing observation uses scoped installation tokens.
internal static class GitHubUserAccess
{
    internal static async Task<GitHubRepositoryAccess> ResolveAsync(
        HttpClient client, string token, long appId, string repositoryUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "github.com" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new McpOperationException("Use the HTTPS URL of the GitHub repository.");
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new McpOperationException("Choose one GitHub repository.");
        }
        var coordinates = GitHubSetupService.ParseUrl(repositoryUrl);
        var expected = $"{coordinates.Owner}/{coordinates.Name}";
        for (var page = 1; page <= 100; page++)
        {
            using var installations = await Read(client, token, $"user/installations?per_page=100&page={page}", cancellationToken).ConfigureAwait(false);
            var items = installations.RootElement.GetProperty("installations");
            foreach (var installation in items.EnumerateArray())
            {
                if (installation.GetProperty("app_id").GetInt64() != appId
                    || installation.TryGetProperty("suspended_at", out var suspension) && suspension.ValueKind != JsonValueKind.Null)
                {
                    continue;
                }
                var installationId = installation.GetProperty("id").GetInt64();
                for (var repoPage = 1; repoPage <= 100; repoPage++)
                {
                    using var repositories = await Read(client, token, $"user/installations/{installationId}/repositories?per_page=100&page={repoPage}", cancellationToken).ConfigureAwait(false);
                    var repos = repositories.RootElement.GetProperty("repositories");
                    foreach (var repository in repos.EnumerateArray())
                    {
                        if (string.Equals(repository.GetProperty("full_name").GetString(), expected, StringComparison.OrdinalIgnoreCase))
                        {
                            return new(appId, installationId, repository.GetProperty("id").GetInt64(),
                                repository.GetProperty("owner").GetProperty("login").GetString()!, repository.GetProperty("name").GetString()!);
                        }
                    }
                    if (repos.GetArrayLength() < 100)
                    {
                        break;
                    }
                }
            }
            if (items.GetArrayLength() < 100)
            {
                break;
            }
        }
        throw new McpOperationException("The user and GitHub App must both have access to the requested repository. Install or configure the App, then connect again.");
    }

    private static async Task<JsonDocument> Read(HttpClient client, string token, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/" + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("DigitalBrain/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new McpOperationException("GitHub repository authorization could not be verified.");
        }
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
