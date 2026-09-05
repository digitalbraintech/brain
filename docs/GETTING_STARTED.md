# Use DigitalBrain from Flutter

Start Docker, configure the AppHost's `Parameters:openai-api-key` user secret for its selected model, then run from the repository root:

```powershell
aspire start --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --non-interactive
```

The AppHost starts storage, the kernel, Flutter Windows and the separate C# scripting host. Flutter connects to `http://localhost:5080`. For deterministic local testing, append `-- --DigitalBrain:Mode=Testing`; this uses fake model responses.

## One workspace

**My brain** starts with Ino. Send a message in the composer; **Full conversation** opens the same history. The graph reveals involved neurons and their real subscriptions and activity. Saved inactive behaviors remain discoverable in **Behavior Studio**.

Select a neuron or an arrow to inspect identity, signals and connections. Create a subscription by choosing a compatible subscriber and signal. Unsubscribe removes that edge. A Bound edge is persistent wiring; a Learned edge records a handled direct request. Temporary activity is distinct from these relationships.

Drag nodes to arrange them, pan or zoom, and use the directory as a list alternative. Server-sent updates refresh changed graph state; reconnect restores a fresh snapshot. There is no fixed two-second full-graph polling loop. Technical participants can be revealed separately. Journals are bounded diagnostic evidence, not durable execution queues.

## Save, change and run a behavior

Open **Behavior Studio**, select **New behavior**, and give it a local name such as `personal-review`. The editor shows source, input/output signal types, optional input policies, draft and active revision, validation diagnostics, activation state, pending work and actual connections.

1. Save the draft. The scripting host compiles it and reports diagnostics.
2. Connect an input source and output recipient using the graph, SDK or Ino.
3. Activate the validated draft. The graph's Bound edges are executable wiring.
4. Use **Run** with a declared input, or let a subscribed source deliver inputs.
5. Edit and save a new draft. Existing work keeps its original revision; activation affects subsequent inputs. Disable removes incoming subscriptions and fences outstanding work while retaining source.

If a draft removes an input signal used by an existing subscription, remove that
connection before activation. The previous active revision remains in place until
the new draft can be activated safely.

A minimal `Note` → `Note` handler:

```csharp
await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
var note = digitalBrain.Input<Note>();
return new Note($"Processed: {note.Text}");
```

The [personal review handler](examples/personal-code-review.csx) asks distinct architecture and quality agent neurons concurrently and returns one combined note. Its input must contain actual review evidence. In Development, Ino can use `read_repository_diff` to obtain the checkout's tracked diff before invoking it. That reader reports untracked filenames and truncation; it does not edit files or post remotely.

Ask Ino to save your preferred review routine, show its C#, connect its output to this conversation, and activate it. Then ask to run the existing behavior with the current diff. Invoking does not rewrite the saved source.

## Compose neurons in C#

A composition program issues durable commands once. A saved handler processes one accepted input. Composition is not replayed after restart.

```csharp
await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
var source = digitalBrain.Get<IWebhook>("build-events");
var behavior = digitalBrain.Get<IBehavior>("build-notifier");
var chat = digitalBrain.Get<IChat>("here");

await behavior.SaveScriptAsync<WebhookReceived, Note>(
    await File.ReadAllTextAsync("build-notifier.csx"));
await chat.SubscribeAsync<Note>(behavior);
await behavior.SubscribeAsync(source);
await behavior.ActivateAsync();
```

The client implementation is in `DigitalBrain.Sdk`; its namespace remains `DigitalBrain.Abstractions`. In a saved handler, `ConnectAsync(args)` borrows its execution connection, principal and current input. Local names are scoped automatically.

A trusted console composition program configures `ConnectionStrings:clustering`, the host's Orleans cluster/service settings, `DigitalBrain:Owner`, and `DigitalBrain:Principal` for the user's GUID. Direct cluster credentials are infrastructure authority; a principal argument is not browser authentication. Do not distribute cluster credentials to untrusted clients.

When exactly one signal type matches, `SubscribeAsync(source)` infers it. Otherwise use `SubscribeAsync<TSignal>(source)`. Getting the same string through two different contracts does not cast a neuron: `IRepository` and a generic `IWebhook` have distinct identities.

## External sources

See [GitHub setup and PR reviews](github-pr-review.md). `IRepository : IWebhook` uses shared SDK receipt, deduplication and retry machinery, and emits `PullRequestChanged` facts.

For a custom signed HTTP source, register it in the kernel:

```csharp
services.AddWebhookSource(new ConfiguredWebhookSource(
    owner, principal, "build-events", "/integrations/build-events", signingSecret));
```

The sender posts JSON with a stable `X-DigitalBrain-Delivery` ID and `X-DigitalBrain-Signature-256: sha256=<hex HMAC-SHA256 of exact request bytes>`. Use a secret of at least 32 characters from private host configuration. Acceptance persists before acknowledgement. `WebhookReceived.Fact` contains `WebhookPayload.Json`. Provider modules can inherit `WebhookNeuron` and emit their own domain signals.

`IXAccount` publishes `NewPost` through synapses, but this checkout does not include an authenticated live X adapter. A subscription alone cannot fetch @elon posts; a provider integration must translate authorized observations into those signals.

## Specialists and diagnostics

Ino delegates ordinary `AgentRequest` calls to Aspire, Gmail and Salesforce using native provider tool schemas. The local Aspire instance is `digitalbrain-local`. Google and Salesforce retain their existing read-only login continuation rules. GitHub has a bounded exact setup continuation for its original repository, behavior and draft revision. Credentials never become source literals or graph labels.

The Development AppHost enables AI trace content through `DigitalBrain:AI:Telemetry:EnableSensitiveData`; other environments default to off. Aspire shows neuron, delegation, model and tool activity. Enabling content later cannot reconstruct omitted content.

## Restart and retained state

Definitions, subscriptions, accepted inputs and request checkpoints are durable. The worker recovers claims after restart without replaying composition. Completed checkpointed agent requests and stable chat output IDs prevent repeated sibling computation and duplicate notes. Current-state reads remain fresh on retry.

Arbitrary file/HTTP effects in trusted C# do not acquire exactly-once semantics. Make such effects idempotent and pass `CancellationToken` to asynchronous work. The host cannot safely terminate arbitrary code that ignores cancellation.

The admitted-script runtime, fixed GitHub review pipeline and automatic migration
adapters have been removed. Current behavior records persist across restart. The
cleanup does not delete local storage; compatibility with older stored records is
tracked separately in the validation record.
