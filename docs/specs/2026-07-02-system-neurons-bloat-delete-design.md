# SystemNeurons Demo Bloat and Surface Ownership Cleanup — Design

**Date:** 2026-07-02
**Status:** Design, pending implementation plan
**Scope:** `brain/` only — primarily `DigitalBrain.Kernel/SystemNeurons.cs` (and minimal related in seeds/bridges if needed for ownership move). No AppHost or hosting changes expected.

## Context

This continues the cleanup/refactoring/simplification initiative (see `docs/plans/2026-07-01-cleanup-refactoring-simplification-30-steps-neuron-synapse-proof-plan.md`, `docs/SYSTEM_DESIGN.md`, `core-requirements/Musk approach.txt`, and the 07-02 neuron-test-harness and fix-pre-existing docs/plans).

**Summary of already-completed work (to avoid duplication):**
- Isolated testable marketplace inos (full 11-task plan + amendment): Core-only interfaces + independent logic for Windows/Developer/Context/UiKit/Telegram.Channel/Google; grains remain in Kernel; pure packs for Telegram/PersonalAssistant; TestKit + dual-DI; Core purity verified; PersonalAssistant embedded.
- NeuronTestBase extraction + migration (8 tasks): base + 14 mechanical file migrations across 6 ino .Tests projects; 31/31 passing; UnitTest1 split + full assertion-quality/test-anti-patterns audit deferred to own plans.
- Fix pre-existing test failures: Task 1 (duplicate PerformKernelSelfUpdate removal + Journal registration) complete per ledger; Software10 interface/feature/sample/empty neuron + related deletes largely done (no remaining references; empty file + other dead removed in follow-up cleanup).
- Authoring loop slices, distribution/bundles phases (manifest/catalog/trust/facet/telegram), and various P0 root-plan items (IFlutterUiNeuron, cross-channel Telegram→UI viz proof, etc.).
- Recent deletes: dead empty `Software10TeamNeuron.cs`, unused `SystemShellHelpers.cs` (logic duplicated locally), stale `start.cs` refs/comments.

Current pain (validated by direct reads/greps in research + brainstorming subagent):
- `SystemNeurons.cs` (esp. `AspireOrchestratorNeuron.HandleAsync(StartDistributedApp)`) contains massive hardcoded demo surface emission (dashboard, marketplace trees from seeds, shell nav, "SE Hello World", INO Chat, chart, kit trees, task trees) + direct `MarketplaceSeeds` usage + bus broadcasts.
- This is special-casing / "god object" ownership that violates the core law (surfaces should be emitted by concerned neurons via typed synapses + `IHandle` or buses).
- After recent dead-helper delete, a local static copy of `BuildShellMenuItems` remains inline.
- Duplication and bloat make the "everything is Neuron or Synapse" proof harder to see and maintain.

The root plan calls for deletes of ad-hoc/hardcoded/demo emission paths, duplication, and special cases. The 07-02 fix design explicitly called out "gateway dispatch duplication" and other slices but deferred; this bloat is a parallel high-delete-leverage item.

## Goals

- Delete as much demo bloat and direct seed/hardcode usage as possible while preserving observable default startup surfaces for dev (`aspire run`) and tests.
- Move surface ownership to the right neurons (e.g. MarketplaceNeuron for list/tree surfaces, dedicated or existing for shell chrome) so emission happens via `FireAsync` of `UiSurface` / `Signal` + buses or direct handlers — pure neuron/synapse.
- Remove duplicated logic (post prior delete).
- Keep changes small/focused/delete-biased; one independent shippable plan.
- Maintain all current behavior for clients (Flutter RFW, E2E, Telegram flows) and tests.

## Non-goals

- Changing the gRPC/UI surface contract or proto.
- Full rewrite of startup orchestration or adding new abstraction layers (Musk: delete/simply first).
- Touching `app/` Flutter code or E2E render fixtures beyond minimal assertion updates if any.
- Touching Gateway string dispatch (separate candidate slice).
- Editing `.superpowers/sdd/` ledgers.
- AppHost/hosting wiring (no aspire doctor required unless we touch).

## Design

### Problem evidence (direct reads)
- `DigitalBrain.Kernel/SystemNeurons.cs:28-39` (and following): literal dashboard props, `FireAsync` + bus broadcast of multiple demo surfaces.
- `...~92-106`: direct `MarketplaceSeeds.LocalUiPacks` for `MarketplaceListFromPacks` + `MarketplaceTreeSurface`.
- `...~110-160`: local `BuildShellMenuItems()` (dupe of deleted helper), scaffold/sidebar/content trees, shell chrome surface with hard nav array.
- Similar for SE hello, tasks, installed bundles, etc.
- `SystemNeurons.cs` + `AspireOrchestratorNeuron` ends up owning UI concerns that belong to other neurons.

Clean model (per SYSTEM_DESIGN + root plan + core law):
- `AspireOrchestratorNeuron` (or thin equivalent) does only: `DistributedAppStarted`, `SystemStatusChanged`, minimal wiring.
- Owning neurons (`MarketplaceNeuron.HandleAsync(...)` on publish/filter, `ChatNeuron`, `DataVisualizationNeuron`, task grains, etc.) or a small dedicated thin `StartupSurfaceNeuron` (if needed) emit their `UiSurface` / widget trees via `FireAsync` + `HomeFeedBus` / `SignalEgressBus`.
- Use existing `UiSurfaceLiveData`, consts, and kit primitives. No hard-coded demo trees in "system" startup.
- Delete the bloat lines + any now-unused helpers/seeds direct reads.

### Changes
- Edit `SystemNeurons.cs`: strip demo emissions to minimum (launched status + one or two core surfaces that have no owner yet); keep behavior-equivalent by ensuring owning paths still fire (they already do in many cases via other handlers).
- If a surface has no natural owner today, either delete the demo or introduce minimal emission from the relevant grain (e.g. on activation or first relevant synapse).
- Delete any remaining inline dupe of menu builder.
- Update any direct assertions in tests that over-specify exact startup tree content (prefer "contains key surface" over full snapshot if brittle).
- No new public APIs; self-explanatory method/variable names.

### Verification
Per guardrail: after every edit `dotnet build` → `dotnet test --filter "<relevant e.g. Startup|Ui|NeuronCore|Marketplace>"` (targeted). No hosting change → no aspire doctor. Full relevant filter + manual sanity at end of plan. Use Context7 for any gRPC/Orleans streaming or grain patterns before any edit that touches them.

## Risks
- Medium: default `aspire run` UI / E2E may rely on exact demo surfaces/nav emitted at start. Mitigate by preserving exact emitted set via owner neurons or minimal core surfaces; run targeted Ui + E2E filters.
- Low: delete bias means some "SE Hello" / demo content may disappear from default dashboard — acceptable per Musk (question if it should exist in core substrate) and root plan (delete more than add).
- No risk to Core purity or IHandle dispatch.

## Suggested sequencing (input to plan)
1. Audit exact surfaces emitted today (read + test run).
2. Strip bloat in SystemNeurons; ensure owning neurons emit (or add tiny emission).
3. Delete dupe logic.
4. Update tests + verify full targeted suite.
5. Update SYSTEM_DESIGN / CONTINUITY if ownership model clarified.
6. Final build + broad filter + (if needed) aspire resource check.

This slice is delete-heavy, small, independent, and directly advances the vision.
