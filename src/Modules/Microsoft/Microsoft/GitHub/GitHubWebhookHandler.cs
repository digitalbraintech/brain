using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Webhooks;
using Octokit.Webhooks;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubWebhookHandler(GitHubRepositoryBinding binding, IGrainFactory grains) : IWebhookHandler
{
    public async Task<WebhookAcceptance> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidateSignature(request.Body.Span, Header(request, "X-Hub-Signature-256"), binding.WebhookSecret))
        {
            return WebhookAcceptance.Unauthorized;
        }

        var delivery = Header(request, "X-GitHub-Delivery");
        var eventName = Header(request, "X-GitHub-Event");
        if (!Guid.TryParse(delivery, out _) || string.IsNullOrWhiteSpace(eventName))
        {
            return WebhookAcceptance.BadRequest;
        }

        using var activity = GitHubTelemetry.Source.StartActivity("github.webhook.accept", ActivityKind.Producer);
        GitHubTelemetry.TagReceipt(activity, binding, delivery!, number: null);
        WebhookReceipt receipt;
        try
        {
            using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 32 });
            var payload = document.RootElement;
            var classification = Classify(payload, eventName);
            if (classification.Failure is { } failure)
            {
                activity?.SetTag("github.webhook.acceptance", failure.ToString().ToLowerInvariant());
                return failure;
            }

            var sha = eventName switch
            {
                "check_run" => String(payload, "check_run", "head_sha"),
                "check_suite" => String(payload, "check_suite", "head_sha"),
                "status" => String(payload, "sha"),
                _ => null,
            };
            if (sha is not null && !GitHubRepositorySource.IsSha(sha))
            {
                return WebhookAcceptance.BadRequest;
            }

            receipt = WebhookTrace.Capture(new(delivery!, Convert.ToHexStringLower(SHA256.HashData(request.Body.Span)), binding.Revision, new RefreshRepository(binding.Id, delivery!, binding.Revision, classification.Number, classification.Revoke, sha, eventName == "ping" ? "ping" : String(payload, "action"), AuthenticatedCallback: true), DateTimeOffset.UtcNow));
            GitHubTelemetry.TagReceipt(activity, binding, receipt.DeliveryId, classification.Number);
        }
        catch (JsonException)
        {
            return WebhookAcceptance.BadRequest;
        }

        using var actor = VerifiedActor.Enter(new ActorContext(binding.Principal, "github-webhook"));
        var source = NeuronId.For<IRepository>(binding.Owner, binding.InstanceName);
        var inbox = grains.GetGrain<IAuthenticatedWebhookIngress>(source.ToGrainId());
        WebhookAcceptance accepted;
        try
        {
            accepted = await inbox.AcceptAsync(receipt).WaitAsync(cancellationToken);
        }
        catch (Exception error)
        {
            GitHubTelemetry.Failed(activity, error);
            throw;
        }

        activity?.SetTag("github.webhook.acceptance", accepted.ToString().ToLowerInvariant());
        return accepted;
    }

    private (int? Number, bool Revoke, WebhookAcceptance? Failure) Classify(JsonElement payload, string eventName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return (null, false, WebhookAcceptance.BadRequest);
        }

        if (eventName == "ping")
        {
            var appId = Number(payload, "hook", "app_id");
            return appId is null || appId == binding.AppId ? (null, false, null) : (null, false, WebhookAcceptance.Unauthorized);
        }

        if (Number(payload, "installation", "id") != binding.InstallationId)
        {
            return (null, false, WebhookAcceptance.Unauthorized);
        }

        var action = String(payload, "action");
        if (eventName == "installation" && action is "deleted" or "suspend")
        {
            return (null, true, null);
        }

        if (eventName == "installation_repositories")
        {
            var removed = payload.TryGetProperty("repositories_removed", out var repositories) && repositories.ValueKind == JsonValueKind.Array && repositories.EnumerateArray().Any(repository => Number(repository, "id") == binding.RepositoryId);
            return removed ? (null, true, null) : (null, false, WebhookAcceptance.Ignored);
        }

        if (Number(payload, "repository", "id") != binding.RepositoryId)
        {
            return (null, false, WebhookAcceptance.Unauthorized);
        }

        if (eventName == "repository" && action is "deleted" or "transferred" or "renamed" or "archived")
        {
            return (null, true, null);
        }

        if (!string.Equals(String(payload, "repository", "name"), binding.RepoName, StringComparison.OrdinalIgnoreCase) || !string.Equals(String(payload, "repository", "owner", "login"), binding.RepoOwner, StringComparison.OrdinalIgnoreCase))
        {
            // A trusted rename/transfer requires explicit reauthorization of the coordinates.
            return (null, true, null);
        }

        if (eventName == "pull_request")
        {
            if (action is not ("opened" or "reopened" or "synchronize" or "ready_for_review" or "converted_to_draft" or "closed" or "edited"))
            {
                return (null, false, WebhookAcceptance.Ignored);
            }

            var number = Number(payload, "number");
            return number is > 0 and <= int.MaxValue ? ((int)number.Value, false, null) : (null, false, WebhookAcceptance.BadRequest);
        }

        // Check payloads may have no PR association for forks. Reconcile authoritative open PRs.
        return eventName is "check_run" or "check_suite" or "status" ? (null, false, null) : (null, false, WebhookAcceptance.Ignored);
    }

    internal static bool ValidateSignature(ReadOnlySpan<byte> body, string? signature, string secret) => WebhookSignatureValidator.Verify(signature, secret, body) == WebhookSignatureValidationResult.Valid;
    internal static string? Header(WebhookRequest request, string name)
    {
        var matches = request.Headers.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 && matches[0].Value.Length == 1 ? matches[0].Value[0] : null;
    }

    private static string? String(JsonElement value, params string[] path) => TryProperty(value, path, out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    private static long? Number(JsonElement value, params string[] path) => TryProperty(value, path, out var found) && found.ValueKind == JsonValueKind.Number && found.TryGetInt64(out var number) ? number : null;
    private static bool TryProperty(JsonElement value, string[] path, out JsonElement found)
    {
        foreach (var part in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
            {
                found = default;
                return false;
            }
        }

        found = value;
        return true;
    }
}
