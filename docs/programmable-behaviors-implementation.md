# Programmable neurons and Behavior Studio

Approved implementation, 5 September 2026. This document records the intended
contract; verification results are recorded separately in `programmable-behaviors-validation.md`.

## One programming model

`IDigitalBrain` addresses neurons. Neurons own durable state and source-owned
synapses. A saved `IBehavior` is a neuron whose typed handler is ordinary C#.
The SDK owns connections, reusable external-event ingress, and execution clients.
Provider modules translate authenticated provider observations into domain signals.
Studio displays and edits these same definitions and relationships.

Composition programs execute graph commands once. They are not automatically
replayed on restart. Behavior handler programs run once for each accepted input.
Editing a handler creates a draft; activating it selects the validated revision.
Existing runs retain their revision, input identity, and request checkpoints.
Disabling fences execution and delivery before reporting completion.

```csharp
await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
var webhook = digitalBrain.Get<IWebhook>("build-events");
var behavior = digitalBrain.Get<IBehavior>("build-notifier");
var chat = digitalBrain.Get<IChat>("here");

await behavior.SaveScriptAsync<WebhookReceived, Note>(
    await File.ReadAllTextAsync("build-notifier.csx"));
await chat.SubscribeAsync<Note>(behavior);
await behavior.SubscribeAsync(webhook);
await behavior.ActivateAsync();
```

The saved handler:

```csharp
await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
var received = digitalBrain.Input<WebhookReceived>();
return new Note($"Received external event {received.EventId}");
```

For provider-specific facts, use `IRepository` and `PullRequestChanged` directly.
`IWebhook` remains reusable for other provider modules and user-configured sources.
The same name under two different neuron contracts does not create the same ID.

## Recovery ownership

| Owner | Normal wakeup | Recovery responsibility |
| --- | --- | --- |
| SDK webhook worker | Durable receipt accepted by its source | The source's one-minute reminder requeues pending receipts and recipient acknowledgments. |
| Behavior executor | Registry journal notification | A 15-second registry pass discovers missed notifications, expired work leases and pending compilation after reconnect. |
| Individual behavior | Short signal/command handling | A 15-second activation timer retries durable subscription and output intents. |
| Running script | Its accepted input | A 10-second lease renewal detects cancellation; execution has a two-minute deadline and bounded retries. |
| Graph event stream | Journal observation | A 30-second observer lease reconnects observations and retries unavailable participants. |

The registry pass still scales with the number of saved behaviors. These changes
remove GitHub-specific workflow polling and duplicate work ownership; they do not
claim a measured throughput improvement or eliminate all recovery scans.

## Implementation work

1. SDK client and native typed composition helpers; source-bound execution avoids
   routing every parallel request through the serialized owner root.
2. Individual behavior definitions, validation, immutable revisions, accepted-input
   queue, leases, request checkpoints, durable output and receiver fencing.
3. Reusable authenticated webhook acceptance, receipt deduplication, independent
   recipient retries, and recovery with no subscriber work in the HTTP request.
4. GitHub source integration, provider setup/readiness, strict required-check
   evidence, and ordinary configurable reviewer neurons.
5. Ino discovery and invocation tools; saved behavior library, editor and revision
   diagnostics in Flutter; one graph with subscriptions and observed activity.
6. Remove the old admission runtime, fixed GitHub review pipeline, compatibility
   adapters and their obsolete tests. Preserve current behavior records and do not
   delete local user storage as part of source cleanup.
7. Build, regression and failure-boundary tests; native Flutter validation using
   Computer Use; document exact results and any external connection prerequisites.

## Acceptance boundaries

- A restart does not erase definitions, subscriptions, accepted work or completed
  request checkpoints. Repeated publication of the same output cannot duplicate
  its chat note, including after transcript retention.
- Slow or failing recipients do not stop authenticated ingress or unrelated
  recipients. Provider receipt identity is distinct from domain observation identity.
- Invalid source, missing connection, unresolved placeholders, and unknown CI
  evidence produce useful diagnostics; they do not masquerade as running monitoring.
- Parallel reviewer calls reach distinct targets concurrently. Failure retries
  preserve completed sibling results. Head/base changes and disable fence old work.
- User/principal isolation applies to definitions, source ingress, setup and graph
  discovery. Credentials never appear in source, graph labels or diagnostics.
- Studio stores presentation state only. Its edges come from neuron synapses;
  request activity is not presented as a persistent subscription.
- Provider access remains subject to an actual authorized provider connection.
  Passing local signed-delivery tests is recorded separately from live-provider tests.
