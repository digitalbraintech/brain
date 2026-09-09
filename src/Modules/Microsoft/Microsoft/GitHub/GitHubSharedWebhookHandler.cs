using System.Text.Json;
using DigitalBrain.Sdk.Webhooks;

namespace DigitalBrain.Microsoft.GitHub;
/// <summary>One physical App endpoint, independent principal-scoped durable sources.</summary>
internal sealed class GitHubSharedWebhookHandler(GitHubRepositoryBindings bindings, IGrainFactory grains) : IWebhookHandler
{
    public async Task<WebhookAcceptance> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        // Reading routing hints grants no authority: each selected handler independently validates exact-byte HMAC.
        long? installation = null, repository = null, pingApp = null;
        try
        {
            using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.TryGetProperty("installation", out var app) && app.TryGetProperty("id", out var id))
            {
                installation = id.GetInt64();
            }

            if (document.RootElement.TryGetProperty("repository", out var repo) && repo.TryGetProperty("id", out var repoId))
            {
                repository = repoId.GetInt64();
            }

            if (document.RootElement.TryGetProperty("hook", out var hook) && hook.TryGetProperty("app_id", out var appId))
            {
                pingApp = appId.GetInt64();
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return WebhookAcceptance.BadRequest;
        }

        var targets = bindings.All.Where(binding => installation is not null ? binding.InstallationId == installation && (repository is null || binding.RepositoryId == repository) : pingApp is not null && binding.AppId == pingApp).ToArray();
        var ping = GitHubWebhookHandler.Header(request, "X-GitHub-Event") == "ping";
        if (ping && installation is null && pingApp is null)
        {
            // App pings need not carry installation or App IDs. Only the configured
            // App secret can select its authorized scopes when those hints are absent.
            var signature = GitHubWebhookHandler.Header(request, "X-Hub-Signature-256");
            targets = bindings.All.Where(binding => GitHubWebhookHandler.ValidateSignature(request.Body.Span, signature, binding.WebhookSecret)).ToArray();
        }

        if (targets.Length == 0)
        {
            return ping ? WebhookAcceptance.Unauthorized : WebhookAcceptance.Ignored;
        }

        var results = await Task.WhenAll(targets.Select(binding => new GitHubWebhookHandler(binding, grains).HandleAsync(request, cancellationToken)));
        // No successful HTTP acknowledgement until every intended destination owns its receipt durably.
        if (results.Any(result => result is WebhookAcceptance.Unavailable))
        {
            return WebhookAcceptance.Unavailable;
        }

        if (results.Any(result => result is WebhookAcceptance.Unauthorized))
        {
            return WebhookAcceptance.Unauthorized;
        }

        if (results.Any(result => result is WebhookAcceptance.Conflict))
        {
            return WebhookAcceptance.Conflict;
        }

        if (results.Any(result => result is WebhookAcceptance.BadRequest))
        {
            return WebhookAcceptance.BadRequest;
        }

        return results.Contains(WebhookAcceptance.Accepted) ? WebhookAcceptance.Accepted : results.Contains(WebhookAcceptance.Duplicate) ? WebhookAcceptance.Duplicate : WebhookAcceptance.Ignored;
    }
}
