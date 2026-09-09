# Programmable DigitalBrain Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for independent tasks and inline TDD for the coupled runtime path.

**Goal:** Execute user/assistant-authored C# behaviors through explicit durable communication, with simple production-path scenarios.

**Architecture:** Public typed ports and application manifests describe the program. Orleans persists revisions, ownership and admitted work; SDK-built artifacts execute pinned handlers. All authoring surfaces share application services.

**Tech Stack:** .NET 11, Orleans, Microsoft.Extensions.AI, Aspire, xUnit v3.

**Spec:** ../specs/2026-09-07-file-based-scripting-design.md

## Global constraints

- No migration or legacy compatibility requirement; retain useful internals only where tested.
- Existing AgentRequest/AgentReply express composed agent requests and results.
- Observe red, implement minimal behavior, observe green, then refactor.
- Production routing/persistence/build/execution in acceptance tests; fake external providers only.
- No behavior-owned expected output may be created by fixture setup.
- Work in existing codex/day-zero-scripting feature checkout; preserve the accepted uncommitted spec.

## Ordered slices

- [ ] 1. Establish baseline. Add a real authored operation scenario to tests/DigitalBrain.Simulation.Tests; fail on missing application semantics. Introduce public application/port definitions in DigitalBrain.Contracts and SDK authoring in DigitalBrain.Sdk. Verify invocation and negative inputs through production transport.
- [ ] 2. SDK file application build/artifact execution in DigitalBrain.Scripting. Test compiling real files, declaration without business effects, immutable artifacts and handler reconstruction; replace Roslyn compilation after consumers move.
- [ ] 3. Durable application registry, explicit owned routes and admissions in DigitalBrain kernel. Test idempotent apply, independent route ownership, same-target distinct inputs and rejected unknown contracts. Remove learned delivery.
- [ ] 4. Durable behavior execution, state/checkpoint transaction, invocation isolation, revision fences and recovery. Test effect/checkpoint fault windows using real persistence and restart controls, never a second fake runtime.
- [ ] 5. Real chat ingress and composed agent behavior. Test /ping -> pong, unrelated input, isolated principal, then proposer/critic/synthesizer using strict external model responses and AgentRequest/AgentReply.
- [ ] 6. Assistant/editor/MCP shared source revision authoring. Test tool-driven source creation, validation, activation, expectation retention and revision conflicts.
- [ ] 7. Startup/configuration and explicit initialization. Rewrite scripts/start.cs, activities.cs and ui.cs. Test late registration, reapply, child dependencies and initialization race recovery. Delete obsolete startup worker/ledger path.
- [ ] 8. Durable parallel composition, event waits, delays and child authoring. Test immediate response race, cancellation, dependency revision selection and bounded failures.
- [ ] 9. Tavily search and Aspire onboarding (independent). Implement AI search provider and tool; HTTP fixture tests assert results/error behavior; hosting tests verify secret parameter wiring and description. Register only when enabled. External credentials remain outside CI.
- [ ] 10. UI events and ingress. Test durable component addition separately from browser rendering and click forwarding. Remove no-op advertised behavior.
- [ ] 11. Whole-solution verification, independent code review, removal inventory and product smoke checks. Record exact checks and unavailable external prerequisites.

## Test loop for each slice

1. Write one scenario with literal expected outcomes and controlled external responses.
2. Run the scenario and record the expected missing-behavior failure.
3. Implement the smallest complete production path supporting it.
4. Run that scenario and relevant existing regressions.
5. Refactor/delete superseded code while those checks remain green.

## Progress and evidence

Implementation in progress; this is not a completion claim.

Current checkpoint (implementation remains incomplete):
- Latest user-driven Aspire/MCP verification found and fixed missing HTTP service discovery and MCP watching the assistant rather than composer reply journal. Both regression tests passed after RED; full Authoring now passes 12/12. Actual MCP tools saved/validated/scenario-tested/activated a chat behavior, recovered it after complete Aspire restart, received a new expected reply and retried without duplicate journal output. Matching v2 storage defaults are now verified live. Aspire remains running; see docs/programmable-behaviors-validation.md for evidence and the fundamental remaining guarantees. Other broad suite totals below predate these two integration fixes.
- Latest full Simulation run passed 93/93 after the current runtime changes. Subsequent focused groups: graph failure + recorded values 2/2; cancellation/cleanup/acceptance 7/7; event-to-neuron and existing routing 8/8; child cancellation/checkpoint/replay 9/9, then sequential/parallel/offline child cancellation 3/3. Substrate also passed 11/11.
- Full Authoring passed 10/10 including immutable expectation history, authenticated HTTP/MCP, UI control ingress and actual production storage options. Full AI passed 17/17 after cancellation surfaces. Flutter shell passed 9/9. Expectation tools passed after observed missing-tool REDs (MCP 1/1, assistant 2/2).
- Actual Aspire startup imported, validated and activated start.cs. A newly saved chat application passed its real chat scenario, activated, and /owner/commands returned one pong. After stopping and restarting the whole app, a new /ping returned pong without recreating the behavior.
- That live check used the initial binary-state namespace and exposed old journal inventory querying deleted behavior grains. Production now uses matching fresh digitalbrain-v2-state and digitalbrain-v2-journal containers, with .digitalbrain/v2/applications for authoring metadata. Production-options test passed; the later MCP smoke above verified these defaults live. Old development data is unreferenced, not migrated. AppHost was stopped again after it reappeared and locked build outputs.
- Explicit event inputs are hidden from callable command descriptions; armed waits handle immediate responses and validate state writes on replay. Durable branches pin selected child revisions. UtcNowAsync/NewGuidAsync record values before returning them.
- A graph worker failure now fails the group promptly, cancels siblings, and drains parent installation as well as children without masking the primary error.
- Explicit invocation cancellation is durable and exposed through SDK, shared authoring, HTTP, MCP and assistant tools. Child command admissions retain parent identity; claim/renew checks cancellation through the parent chain, including after restart. Completed results remain retained. Cooperative running cancellation is observed on the renewal cadence.
- App-owned events now reach explicitly registered neuron inputs with stable causal identity and deduplication. ActivityChanged uses the existing public type and stable codec.
- Expectations can be recorded independently through SetExpectationsAsync with CAS, idempotent operation identity, verified principal and timestamp. Source edits preserve that history; reports bind source/artifact/expectation revisions. Generic acceptance.json edits cannot change an authoritative expectation record. The optional acceptance.json path still exists when no record was explicitly established; mandatory adoption and complete user-intent provenance remain unfinished.
- Remaining: mandatory accepted expectations across assistant/editor/MCP, correlated wait deadlines/budgets, recoverable dynamic behavior creation with bounded authority, full multi-step/UI/provider scenarios, graph atomicity and source provenance, packaged SDK distribution, retention, independent final review and complete regression/live verification. Live provider credentials and real-model unseen-instruction evaluation are not covered by deterministic fixtures.
- No commits. One earlier recursive deletion of task-created runtime smoke records was blocked by automatic approval review; it was not retried. Those records remain ignored/unreferenced.

Earlier checkpoint history:
- Shared starter template through assistant save/validate/activate/invoke passed against the real process supervisor (1/1).
- Chat ingress suite passed all eight cases, including case-insensitive contains matching. Ambiguous contains rules failed before overlap validation; programming suite then passed all six cases.
- Child graph scenario passed after fixing normal supervision cancellation: child source changes effective revision; retained old graph configures once; child operation remains callable.
- Authored completion-only OpenSurface failed first on missing SendAsync, then missing explicit codec; after both implementations it passed across completed configuration reapply and silo restart. This does not prove the narrower delivery-before-checkpoint crash window.
- Flutter editor scenarios passed 2/2 and actual button callback passed 1/1. HTTP button ingress verification exposed fixture DI configuration and is not green yet.
- Exactly-one RunAsync validation and late initialization without OnApply produced expected REDs; fixes await verification.
- Durable delay produced clean missing DelayAsync RED; implementation is in progress. The test waits for persisted waiting status before restarting.

- Baseline simulation: 1/1 passed before changes.
- Explicit routing: observed expected red (learned-only route delivered 1 instead of 0), then substrate 11/11 passed without warning suppression. Learned edges remain observations.
- Application operation foundation: initial missing-API compile red; persisted admission/reconstructed worker test green. Concurrent handler test failed by timeout before concurrent serving implementation, then passed. Additional missing-codec and foreign-actor tests failed before fail-closed fixes and then passed (4 focused tests).
- Review ruling: local delegate reconstruction is not artifact-bound execution. Lifecycle methods and Orleans protocol were internalized; the public RunAsync path requires a verified artifact host. Full process supervision and authenticated worker tickets remain required.
- Initialization: test observed DigitalBrainActivated on an ordinary operation (red), then passed after removing eager initialization from connection/message transport. Explicit activation remains idempotent in this scenario; crash-safe initialization outbox is still pending.
- Compiler: root-source snapshot and artifact closure compiled with Microsoft SDK. Concurrent source edit exposed a provenance gap; reject changed source before publication. Full source dependency snapshot remains pending.
- Search/Aspire: 7/7 normal tests passed, including real DI resolution, stale tool-context validation, secret redaction and Aspire parameter wiring. Actual AppHost enables search outside testing mode; no live secret/provider call was made.
- Artifact integration: 5/5 compiler/host tests passed. Source-change rejection, closure verification, shared assembly identity and borrowed connection ownership covered. In-process hosting remains a foundation, not the required process supervisor.
- Chat: 3/3 production-ingress tests passed for compiled `/ping`, fallback and repeated command identity. Security/failure/tombstone hardening is in progress; no unbounded exactly-once output claim.
- Recovery: a fresh silo reads file-backed Orleans state; admitted old-revision work returns the old result after a new head is installed. This tests persisted application grain state, not persistent neuron journal/outbox crash recovery.
- Composition: missing execution-context invocation API produced compile red; distinct child inputs and worker restart reuse passed. Eight waiting parents then exposed worker-slot starvation (timeout red); removing the artificial eight-task gate made all three checkpoint tests pass. Pending admission remains bounded.
- Source authoring: missing service API compile red observed. Shared save/read/list/validate/activate service is in progress with expected-revision checks and retained artifacts; assistant/editor/MCP consumers have not been replaced yet.
- State: missing `Behavior` API compile red, then increment/call/worker-restart scenario passed with persisted input-start state, checkpointed writes and serialized shared-state scopes. The fixture now also restarts the silo against file-backed storage and awaits verification.
- Determinism: skipping a recorded call after recovery initially completed silently (red); final effect-count validation now rejects it. Checkpoint suite 4/4 passed afterward.
- Full simulation checkpoint: 28/29 passed; the failure exposed Windows SDK temporary-path limits when source filenames contain full hashes. Short basename with hash retained in the source directory fixed it; authoring/compiler/gate focused suite then passed 8/8.
- AI composition: normal AI suite 11/11 passed with proposer/critic/synthesizer, worker replay without repeating the completed proposer, changed-prompt determinism rejection and codec collision checks. Binding a composed operation to a separately resolved `IAgent` is the next slice.
- Definition phase: compiled top-level initialization caused the intended red when the gate was disabled. The restored gate rejects SDK business actions during definition and invalidates a handler's execution permit when it ends. Application-port submissions need the same boundary review.

Ruling: keep unsupported custom wire contracts rejected rather than accepting Alias alone. Explicit registered codecs and schemas will precede module event integration.
Ruling: serialize test builds against shared Windows outputs; parallel source work is allowed, competing rebuild/test processes cause DLL locks.
- IAgent bridge: a separately resolved composed agent now calls other agents without blocking the owner root or application kernel; composed suite 4/4 passed. Removed the superseded recorded-root-send implementation. Fresh file artifact facade/assembly identity is being verified separately.
- Queries: missing Query/QueryAsync compile RED, then snapshot reads (including querying the same behavior from a command) passed. A captured command port initially executed inside a query (RED); query execution now rejects business effects. Query suite 2/2 passed.
- Definition submissions: expected exception was missing (RED); application command submission now uses the definition-phase guard. Focused test passed.
- Generic events: missing Event/Handle/Connect compile RED, then publication/real counter consumption passed. Foreign-principal ports and duplicate owned connections each failed before enforcement/dedup fixes; event suite 3/3 passed afterward. Publication acknowledges acceptance, not recipient completion.
- Supervision: saved source is automatically served and recovers after silo restart. Malformed metadata exposed background-service shutdown failure; invalid records now log and skip. A Guid formatting mismatch then rejected valid records; canonical principal formatting fixed it. Supervisor acceptance passed with malformed record present.
- Application callbacks: successful unchanged revision configuration runs once (GREEN). Failed candidate configuration incorrectly replaced the active head (RED); stage-before-configuration activation fix is under verification.
- Assistant tools: real tool-driven save/validate/activate acceptance added after missing adapter RED. The old Assistant.Behaviors tools were removed; shared-service adapter is implemented and awaiting artifact-path GREEN.
- Assistant invocation: real save/validate/activate tool path reached the expected missing `invoke_application` RED. Added declared-operation invocation and retained result identity to the shared service, HTTP/MCP and assistant tools. GREEN pending: production subprocess supervisor exposed that InProcessTestCluster advertises an in-memory-only gateway; TCP fixture correction is underway.
- Invocation status: missing `ReadInvocationAsync` compile RED observed. Added shared retained-status surface; retry-across-head-change acceptance is awaiting the controlled build lane.
- Configuration initialization: missing `EnsureInitializedAsync` compile RED observed; checkpointed explicit owner initialization implementation awaits GREEN.
- Child graph: real SDK publish produced missing `Script` / `ApplyScriptAsync` RED. Immutable graph inspection and installation implementation is in progress.
- Process authorization review: bootstrap nonce is not sufficient runtime authority. A pending negative test must prove claims require server-issued revision-scoped worker authorization before this boundary is considered complete.
- Process recovery: real TCP cluster + two distinct worker OS processes passed; killing the first worker and advancing its lease allows the replacement to complete the original command (1/1). No gateway mock.
- Assistant saved-program invocation through real subprocess supervisor passed (1/1). Retained invocation status and retry across changed head passed in the integrated basic group.
- Worker authorization: direct same-principal claim without a capability failed the negative test before enforcement; signed app/revision capability checks on claim/lease-owned mutations plus process recovery then passed (2/2). Authority currently assumes the single-silo process; multi-silo issuance is not yet supported.
- Trusted fixture workers now explicitly acquire and revoke silo-issued capabilities via BrainSimulation helpers; the production claim checks remain active.
- Basic integrated group passed 8/8: typed operation lookup without caller handlers, invocation retry/status, finite initialization completion, and real OpenSurface-to-authored-handler delivery.
- Default UI subscription replay failed by delivering a historical CommandId instead of the current one; cursor/typed module registration fix awaits GREEN.
- Contains-chat trigger: actual SDK publish failed on missing OnUserMessageContaining; implementation awaits GREEN and ambiguity coverage.
- Editor analyzer clean and original widget scenario passed. New-app flow exposed an early-disposed dialog controller (fixed) and invalid hardcoded starter (RED). Editor now loads shared server template; both widget scenarios passed (2/2). Application template tool backend still awaits real compilation acceptance.
