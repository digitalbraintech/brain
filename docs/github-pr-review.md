# GitHub sources and authored applications

A configured repository is a domain event source: `IRepository : IWebhook`. The GitHub module validates signed webhook receipts, stores accepted delivery identity durably, and emits repository facts such as `PullRequestChanged`. Review policy belongs in an authored application.

The repository source does not automatically create, connect, or activate an application. Author source through Application Studio, the assistant authoring tools, or MCP; validate it; activate the validated revision; and explicitly configure its typed connections.

## Operator setup

Create a GitHub App with repository read permissions for Contents, Pull requests, Checks, Commit statuses and Administration for required-check discovery, plus Metadata. Install it on the intended repositories. The backend exchanges its private key for an installation token scoped to the selected numeric repository and read permissions. The short-lived user OAuth token establishes the intersection of that user's access and the App installation.

The AppHost configuration includes:

```json
{
  "DigitalBrain": {
    "Microsoft": {
      "GitHub": {
        "App": {
          "AppId": "12345",
          "Slug": "your-app-slug",
          "ClientId": "your-client-id",
          "PublicOrigin": "https://brain.example.com",
          "PublicWebhookUrl": "https://brain.example.com/integrations/github/webhook"
        }
      }
    }
  }
}
```

Set the private AppHost parameters `github-app-private-key`, `github-app-webhook-secret`, and `github-app-client-secret` through user secrets or Aspire. They are projected only to the kernel and never enter application source or graph metadata.

Register `<PublicOrigin>/integrations/github/callback` as the OAuth callback and use `/integrations/github/login` to connect. OAuth uses PKCE, a correlated one-use browser capability, and a bounded lifetime. Configure the App webhook URL as `PublicWebhookUrl` with the same signing secret. Subscribe to the repository event families required by your application.

The shared webhook endpoint authenticates each observation and routes it to matching authorized repository sources. Each source persists its receipt before success is returned, so provider retries do not duplicate an already accepted delivery. A signed ping or event is required before readiness reports verified ingress. The repository does not create a public tunnel or modify GitHub App configuration.

## Author the review application

An application can declare a typed handler for `PullRequestChanged`, read current repository evidence, call named review agents, and publish a typed result through a declared output port. Use the ordinary file format described in [Getting started](GETTING_STARTED.md). The former `.csx` handler examples and inferred input/output policy model were removed.

Keep policy explicit in the source:

- reject draft or closed pull requests;
- require complete head/base and CI evidence;
- recheck the current revision before emitting a result;
- make external effects idempotent and honor cancellation;
- connect the application's declared input and output ports explicitly.

Validation compiles the source without executing business effects. Activation applies a verified artifact. Work accepted before a later activation remains pinned to its admitted revision. There is currently no shipped GitHub-specific application or automatic PR-review wiring.

## Reliability and operations

The HTTP adapter validates HMAC over exact bytes, payload identity, and routing before acceptance. Stable delivery IDs deduplicate retries; a conflicting body returns conflict. Storage failure produces a retryable failure rather than a false success. Provider processing happens outside source turns.

Access revocation and repository connection state are durable. Required branch or ruleset checks must be discoverable and nonempty before they count as ready. Unknown, ambiguous, inaccessible, and unsupported requirements do not count as green.

Authored C# runs as trusted code in supervised scripting worker processes. Kernel-backed commands, waits, and recorded effects have durable identities; arbitrary external file or HTTP effects require their own idempotency. This implementation does not post GitHub comments, reviews, or approvals.

Provider references: [user access tokens](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app), [installation APIs](https://docs.github.com/en/rest/apps/installations), [required status-check protection](https://docs.github.com/en/rest/branches/branch-protection#get-status-checks-protection), and [webhook validation](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries).
