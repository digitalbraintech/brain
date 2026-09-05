# Programmable behaviors: validation record

Date: 5 September 2026. Workspace: `D:\digitalbrain`. Changes are uncommitted.

## Implemented scope

- Typed SDK composition addresses real neurons with `Get<T>()`, saves individual
  `IBehavior` programs and establishes source-owned subscriptions.
- The scripting host compiles and executes saved C# outside neuron delivery turns.
  Drafts, immutable active revisions, accepted inputs, leases, request checkpoints,
  retries and output delivery belong to the individual behavior.
- Reusable SDK webhook ingress durably accepts authenticated receipts before HTTP
  success. Provider modules supply domain facts and connection policy.
- The GitHub path resolves authorized repository URLs, resumes setup, verifies
  signed webhook reachability and discovers required checks before activation.
  The saved review script owns CI gating and concurrent reviewer policy.
- Behavior Studio edits these definitions and displays actual subscriptions and
  observed activity. The former admission runtime and migration adapters are removed.

## Automated verification

The final cleanup builds completed with zero warnings and zero errors.

| Suite | Result |
| --- | --- |
| Substrate | 112 / 112 passed |
| Simulation | 263 / 263 passed |
| Scripting | 30 / 30 passed |
| Live E2E | 44 / 44 passed |
| Aspire hosting | 62 / 62 passed |
| Flutter core | 58 / 58 passed |
| Flutter kit | 68 / 68 passed |
| Flutter shell | 60 / 60 passed; analysis reported no issues |

The simulation run uses four test threads and scripting uses two. An earlier
simulation run exceeded four short deadlines during concurrent compilation;
the complete rerun passed without source changes, skipped cases or relaxed
assertions. The live E2E suite verifies actual save/compile/execute/output graph
events, observer lifetime through garbage collection, unavailable-node recovery,
and the isolated kernel port. Desktop results are still being collected.

The regression suite exercises duplicate receipts and conflicting identities,
partial recipient failure, restart recovery, principal isolation, interrupted
subscribe/unsubscribe, revoked source authority, active revision replacement,
invalid drafts, compiler/runtime fingerprint changes, changing PR heads and base
commits, strict CI evidence, actual concurrent agents, completed request recovery,
and duplicate-free chat publication after transcript retention.

The focused runtime suite passed 15 tests after the last cancellation correction.
It includes output paused between a downstream behavior and its recipient, a
self-subscription, a cycle, and two independent cancellation roots released at the
same outgoing-fence barrier. Each cancellation independently traverses the required
recipient fences before acknowledgment; another traversal cannot clear its work
and cause premature success. Normal signal handlers remain serialized.

## Native Windows Flutter verification

The native client is exercised through the Computer plugin. The first pass used
Testing mode with controlled provider/model implementations. The later cleanup
pass starts the normal development AppHost against its existing persistent volume.
Neither pass substitutes for live provider authorization and delivery checks.

The first desktop pass found an unavailable persisted Aspire integration causing
the graph event stream to fail. Projection and observer failures are now isolated
per neuron, with an unavailable state and recovery on the observer lease. The
remaining graph can continue to display healthy neurons and their real edges.

The pass also found a stale “awaiting compilation” message after successful
compilation. Studio now updates that message without replacing unsaved source.
New behavior source demonstrates `ConnectAsync(args)` and `Input<Note>()`.

Normal `aspire start --no-build` returned the dashboard at
`https://localhost:17197` and the kernel reached HTTP 200 / Healthy. The native
client retained the existing conversation, the valid `c` draft and the
`github-pr-review` draft with configuration diagnostics.

The existing-volume check found deleted signal aliases in historical journal
entries for the assistant, session and old behavior registry. Those reads made
Ino appear unavailable, despite current definitions loading. The generic historical
journal reader now preserves unknown entries as non-Signal metadata, keeping their
original stored bytes and sequence positions. Three focused regressions pass for
the actual removed alias, reactivation, mixed/unknown-only reads and watches,
byte preservation, known corruption and an unknown alias followed by truncation.
The fallback recognizes an explicit unresolved Orleans type header at the failed
read position and validates the complete framing. Ordinary live deserialization
remains strict. The full substrate (112), simulation (263) and live E2E (44)
suites passed with this correction. The native recheck is pending.
No local repository/webhook blobs
were present, so this pass cannot prove readability of previously migrated
GitHub receipt payloads.

Native lifecycle and restart checks are in progress.

## Aspire coexistence

Normal `aspire start --no-build` was launched while the isolated E2E AppHost was
live (test PID 28224, DCP PID 20920, kernel port 15417). The CLI selected the normal
AppHost PID 35604 and returned `https://localhost:17197`, while the test kernel
returned HTTP 200. The E2E suite completed 44/44 and cleaned up its own host;
normal startup remained independent. Its first 45-second kernel wait expired
while dependencies and the kernel process were starting; the next readiness check
returned Healthy immediately. The native app uses the normal kernel on port 5080.

## External prerequisites and limits

No live GitHub OAuth authorization, App installation, public webhook registration,
or real GitHub PR review was performed during these local checks. The operator
must supply the App configuration and public callback/webhook origins described
in [the setup guide](github-pr-review.md). The user then connects the requested
repository. Studio and Ino retain a draft until the connection, signed-delivery
proof and required-check evidence are ready.

The generic source abstraction supports additional providers. The current
`IXAccount` source can publish typed `NewPost` facts, but a live authenticated X
stream or polling adapter is not implemented. It must not be presented as a live
monitor of an arbitrary account.

Saved C# is trusted local application code. The standalone SDK client connects to
the configured Orleans cluster using application infrastructure credentials.

See [the implementation contract](programmable-behaviors-implementation.md) and
[the getting-started guide](GETTING_STARTED.md) for the public API and workflow.
