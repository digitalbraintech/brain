# Continuation — programmable neurons and Behavior Studio

The user approved the simplified architecture and implementation, then explicitly
requested removal of the remaining legacy code, investigation of missing Aspire
dashboard URLs, and final native Flutter testing through Computer Use.

## Current architecture

- Each `IBehavior` owns saved C# drafts, its active revision, durable input work,
  leases, request checkpoints and output delivery. `IBehaviors` is only the index.
- The separate scripting host runs ordinary SDK programs using
  `DigitalBrainClient.ConnectAsync(args)`, `Get<T>()` and typed subscriptions.
- Source-owned Bound/Unsubscribe/Broadcast remains authoritative. Learned edges
  represent handled direct requests. Behavior cancellation propagates through
  retained input provenance, including independent concurrent cyclic closures.
- SDK `IWebhook`/`WebhookNeuron` owns authenticated durable receipt handling.
  `IRepository : IWebhook` is the GitHub source; saved C# owns review policy.
- Ino exposes save/read/list/connect/activate/invoke/disable tools. Studio edits
  the same definitions and shows real graph relationships and observed activity.
- The old admission runtime, fixed review/inbox/dispatcher, migration adapters,
  old wrapper contracts and their obsolete tests/docs are removed. No local
  database or user storage was deleted.

## Startup diagnosis

The user's `aspire start` at 2026-09-05 14:31 UTC attached to a concurrently running
E2E AppHost for the same project while its own child CLI was still building. The
E2E host had no dashboard. The parent reported success without a URL, exited, and
the child detected the exited launcher and cancelled its build. Relevant logs:
`C:\Users\vhorb\.aspire\logs\cli_20260905T143121_66849e84.log` and its
`cli_20260905T143121933_detach-child_902a1a8b149c43cbb5921d3bc5a54fdd.log`.

The E2E fixture now gives CLI discovery the test assembly identity and randomizes
proxyless project ports too. Normal startup is `aspire start --apphost
src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --non-interactive`.
Use Aspire state to get current endpoints. Do not reuse stale process IDs.

## Verification and remaining work

The authoritative, current results are recorded in
[the validation record](docs/programmable-behaviors-validation.md). Cleanup passed
Substrate 109, Simulation 263, Scripting 30, E2E 43, Aspire hosting 62 and Flutter
core/kit/shell 57/68/58 before the historical-journal correction below. The normal
AppHost then started with its dashboard URL and kernel health 200. Native Studio
retained the valid `c` draft, `github-pr-review` diagnostics and existing chat.

Existing storage contains removed signal aliases in historical assistant,
session and registry journals, making Ino unavailable in the graph. The current
generic fix adds non-Signal `UnknownJournalEntry` metadata to journal reads,
preserves original bytes and sequences, and catches only Orleans missing-type
resolution. Ordinary delivery and corrupt encoding stay strict. Private graph
history shows safe markers; shared unknown history is redacted. The final
compatibility-only `db.unrouted` codec is also removed. Core and graph agents are
verifying this patch before the next native pass. Do not erase user storage.

The normal AppHost is currently stopped for that rebuild. Next: finish affected
regressions; start normal Aspire alongside the isolated compiled E2E host to prove
discovery/port coexistence; then complete native save/activate/subscribe/run,
invalid-draft, disable and restart checks. Leave QA fixtures disabled and the
normal AppHost healthy. Do not report native lifecycle verification complete yet.

Native QA earlier found an unavailable persisted Aspire neuron interrupting the
whole graph; per-neuron read/watch isolation is now implemented. The graph observer
must retain its callback target strongly because Orleans otherwise holds it weakly.
Studio preserves unsaved edits across compiler updates and starts new source with
the requested SDK ConnectAsync/Input pattern. A temporary saved behavior named `c`
was created through native Studio; leave validation fixtures disabled after QA.

Computer Use was stopped in the previous turn with Escape. The user's current
turn explicitly reauthorizes final Computer testing. Use the Computer plugin's
`@oai/sky` API through node_repl; refresh returned window handles after relaunch.

Live GitHub OAuth/App installation/public webhook delivery and real PR review
have not been performed. Follow [GitHub setup](docs/github-pr-review.md).
`IXAccount` supports typed `NewPost`, but there is no live authenticated X adapter.
No commit or external provider configuration change has been made.
