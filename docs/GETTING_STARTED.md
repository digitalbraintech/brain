# Use DigitalBrain from Flutter

Start Docker, configure the AppHost's `Parameters:openai-api-key` user secret for its selected model, then run from the repository root:

```powershell
aspire start --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --non-interactive
```

The AppHost starts storage, the silo with its shared application authoring supervisor, and the Flutter client. Open the endpoints reported by the Aspire dashboard; development ports are assigned by Aspire. For deterministic local testing, append `-- --DigitalBrain:Mode=Testing`; this uses fake model responses.

## One workspace

**My brain** starts with Ino. Send a message in the composer; **Full conversation** opens the same history. The graph reveals involved neurons and their real subscriptions and activity. Saved and validated applications are managed in **Application Studio**.

Select a neuron or an arrow to inspect identity, signals and connections. Create a subscription by choosing a compatible subscriber and signal. Unsubscribe removes that edge. A Bound edge is persistent wiring; a Learned edge records a handled direct request. Temporary activity is distinct from these relationships.

Drag nodes to arrange them, pan or zoom, and use the directory as a list alternative. Server-sent updates refresh changed graph state; reconnect restores a fresh snapshot. There is no fixed two-second full-graph polling loop. Technical participants can be revealed separately. Journals are bounded diagnostic evidence, not durable execution queues.

## Save, validate and activate an application

Open **Application Studio**, choose an application key, and edit an ordinary C# file. Saving stores the exact UTF-8 source under your authenticated principal and uses an expected source revision to reject stale edits. Validation compiles an immutable artifact and reports diagnostics without changing active behavior. Activation applies the validated revision; accepted commands remain pinned to the revision that admitted them.

A minimal command application is:

```csharp
#:project ../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
#:property TargetFramework=net11.0
#:property PublishAot=false

using DigitalBrain.Abstractions;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var app = brain.Application("greeting");
app.Command<string, string>("reply", (message, _, _) =>
    Task.FromResult(message == "/ping" ? "pong" : "ignored"));
await app.RunAsync(args);
```

Project directives are resolved by the .NET file build without generated project rewriting. The compiler retains the exact root source and hashes the published closure. Child file scripts are compiled and validated as an explicit application graph. Absolute project directives remain a portability limitation.

Applications declare commands, event handlers, state, durable delays and typed event ports through `brain.Application(key)`. Side effects belong in `OnApply` or operation handlers. Saving and validation never execute business effects. The scripting supervisor runs activated artifacts outside the silo process and recovers retained revisions needed by accepted work.
An optional `acceptance.json` records the original instruction and literal command examples:

```json
{
  "instruction": "Reply pong when I invoke reply with /ping.",
  "examples": [
    {
      "name": "ping replies pong",
      "operation": "reply",
      "inputJson": "\"/ping\"",
      "expectedJson": "\"pong\""
    }
  ]
}
```

Save this file with the same expected source revision, validate the candidate, then choose **Run examples**. The runner uses the production runtime in isolated brain identities and stores actual results. It supports declared operations and `chat.user-message/v1` stimuli. UI click and multi-step scenario drivers remain unfinished.

Assistant and MCP tools can record the same document with `set_application_expectations`. This creates an immutable expectation revision separate from editable source files. Later source edits retain it, and activation requires a passing result for the exact source, artifact and expectation revision. Replace expectations explicitly with their current revision; use `read_application_expectations` to inspect retained records. Without a separately recorded expectation set, the current implementation still permits activation without examples and gates only applications that contain `acceptance.json`; mandatory adoption remains planned.

## External sources

See [GitHub setup and PR reviews](github-pr-review.md). `IRepository : IWebhook` uses shared SDK receipt, deduplication and retry machinery, and emits `PullRequestChanged` facts.

For a custom signed HTTP source, register it in the kernel:

```csharp
services.AddWebhookSource(new ConfiguredWebhookSource(
    owner, principal, "build-events", "/integrations/build-events", signingSecret));
```

The sender posts JSON with a stable `X-DigitalBrain-Delivery` ID and `X-DigitalBrain-Signature-256: sha256=<hex HMAC-SHA256 of exact request bytes>`. Use a secret of at least 32 characters from private host configuration. Acceptance persists before acknowledgement. `WebhookReceived.Fact` contains `WebhookPayload.Json`. Provider modules can inherit `WebhookNeuron` and emit their own domain signals.

## Specialists and diagnostics

Ino delegates ordinary `AgentRequest` calls to Aspire, Gmail and Salesforce using native provider tool schemas. The local Aspire instance is `digitalbrain-local`. Google and Salesforce retain their existing read-only login continuation rules. GitHub has a bounded exact setup continuation for its original repository connection request. Credentials never become source literals or graph labels.

The Development AppHost enables AI trace content through `DigitalBrain:AI:Telemetry:EnableSensitiveData`; other environments default to off. Aspire shows neuron, delegation, model and tool activity. Enabling content later cannot reconstruct omitted content.

## Restart and retained state

Definitions, subscriptions, accepted inputs and request checkpoints are durable. The worker recovers claims after restart without replaying composition. Completed checkpointed calls replay their recorded result. Stable operation and effect identities prevent repeating already completed kernel-backed work.

Arbitrary file/HTTP effects in trusted C# do not acquire exactly-once semantics. Make such effects idempotent and pass `CancellationToken` to asynchronous work. The host cannot safely terminate arbitrary code that ignores cancellation.

The former saved-behavior runtime, fixed GitHub review pipeline, and automatic migration adapters have been removed. Existing local records are not migrated by this change.

The redesigned runtime uses `digitalbrain-v2-state` and `digitalbrain-v2-journal` Azure containers. Saved applications default to `.digitalbrain/v2/applications` under the silo content directory; `DigitalBrain:ApplicationStore` can override that path. These stores belong together. The defaults start fresh rather than loading incompatible legacy behavior records.
