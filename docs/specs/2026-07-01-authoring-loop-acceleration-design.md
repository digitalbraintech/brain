# DigitalBrain — Authoring Loop Acceleration (Warm Dev Cluster + Invisible Gate)

- **Status:** Approved for planning
- **Date:** 2026-07-01
- **Owner:** Vladyslav Horbachov
- **Repo:** `digitalbraintech/framework` (`brain/`)
- **Relates to:** `docs/specs/2026-07-01-distribution-and-bundles.md` §6 (the Phase 0 authoring loop this accelerates). Does not touch Phase 1/2 of that spec.

---

## 0. TL;DR

The Phase 0 "author → test → watch it render" loop is **real, not aspirational** — the fast in-process loop (`BundleHarness`) and the full-stack render loop (`LiveRenderVerifier` + real Playwright + real Flutter) both work today, proven by passing tests. The friction is not correctness, it's **cycle time and ceremony**: every render-test run boots a brand-new Aspire cluster from scratch (30–120s) and requires three manually-remembered environment variables plus a manual `flutter build web` step.

This spec closes that gap with two code facts we verified directly, which make the fix smaller than it looks:

1. `GeneratedNeuron.TryEmbody` (`DigitalBrain.Kernel/SystemNeurons.cs:975-998`) unconditionally disposes and re-embodies on every `NeuroPackInstalled` delivery — no version check. Republishing the same pack name+version with edited code always hot-swaps.
2. The Flutter web bundle is served via `PhysicalFileProvider` reading live from disk (`DigitalBrain.Kernel/Program.cs:180-185`) — a running kernel serves a freshly-built bundle immediately, no restart required.

Together these mean a **long-lived, already-running kernel** can absorb an edited bundle or a rebuilt Flutter bundle without ever being restarted. The fix is to let render tests attach to such a warm process instead of always booting a fresh one.

---

## 1. Ground truth (what exists today)

Verified by direct code reading and passing tests, not spec language:

| Capability | Real? | Evidence |
|---|---|---|
| Fast in-process loop (`BundleHarness`, `dotnet test`, <1s, zero deps) | ✅ Real | `DigitalBrain.Tests/Authoring/StarterBundleTests.cs` passes |
| Full-stack render loop (real Aspire, real Chromium, real Flutter Semantics assertions) | ✅ Real, gated | `DigitalBrain.Tests/E2E/ExperienceFlowDriver.cs`; gated by `RUN_FLUTTER_E2E=true` + pre-built web bundle |
| N+1 embodiment, no kernel restart | ✅ Real | `HandlerGrowthTests`, `PackBroadcastReactivityTests` pass |
| Re-publish/re-install same pack name+version → re-embodies with new code | ✅ Real | `SystemNeurons.cs:975-998` (`TryEmbody` unconditional dispose+re-embody) |
| Flutter bundle served live from disk, no kernel restart on rebuild | ✅ Real | `Program.cs:180-185` (`PhysicalFileProvider` over `DIGITALBRAIN_WEBROOT`) |
| AI-generation-from-spec wired into the authoring loop | ❌ Not implemented | Out of scope for this spec — tracked separately, not part of this slice of work |

**Root cause of the friction:** `DigitalBrainAppHostFixture.InitializeAsync()` (`DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs:27-76`) calls `DistributedApplicationTestingBuilder.CreateAsync(...)` and boots an entirely new Aspire application — fresh Orleans cluster, fresh Ollama/Azurite containers — every time the fixture is constructed. From `dotnet test` or VS Test Explorer, that's a new process and a new fixture instance on every single run. This is the correct trade-off for CI isolation (each CI run gets a guaranteed-clean cluster via a unique `ClusterId`), but it's the wrong trade-off for an interactive edit-test-watch loop.

**Also verified (simplifies the design):** three environment variables the fixture sets (`DIGITALBRAIN_TEST_MODE`, `DIGITALBRAIN_USE_LOCAL_MARKETPLACE`, `DIGITALBRAIN_SURFACES_ENABLED`) are set in multiple places (`AppHost.cs`, `DigitalBrainBuilderExtensions.cs`, the fixture) but read nowhere in the current codebase — they are vestigial. Only `DIGITALBRAIN_WEBROOT` is functionally consumed (`Program.cs:179`). The warm-cluster path does not need to replicate the vestigial three.

---

## 2. Design: attach-with-fallback

### 2.1 The warm cluster is a bare `DigitalBrain.Kernel` process, not a new Aspire launch profile

`Program.cs:17-53` already has a documented, working non-Aspire-hosted "fast path": when none of `ConnectionStrings__clustering` / `grainstate` / `journal` are present (i.e. run outside Aspire), the kernel binds to **fixed Kestrel ports** — `8080` (gRPC, HTTP/2-only, cleartext) and `8081` (web + gRPC-Web + MCP, HTTP/1.1+HTTP/2) — and uses in-memory Orleans clustering/journals. This is an existing, intentional code path (see the comment at `Program.cs:19`), not something this spec invents.

The warm dev cluster is simply: **run `dotnet run --project DigitalBrain.Kernel` with `DIGITALBRAIN_WEBROOT` pointed at the built Flutter bundle, and leave it running.** No new AppHost launch profile, no port-picking exercise, no Ollama/Azurite/observability graph — the content bundles exercised by the authoring loop (`KitExperience`-based, e.g. hello-world, ui-gallery, starter-bundle) don't need an LLM to embody or render. This is a **deletion**, not an addition, relative to the original sketch of this design (which proposed a new fixed-port Aspire launch profile) — the fixed ports already exist and are already tested-path-adjacent.

State is in-memory, so **restarting the warm cluster is the reset mechanism** if a dev session's state ever gets confusing — there is no persisted store to clean up.

### 2.2 Fixture change: probe, attach, or fall back

`DigitalBrainAppHostFixture.InitializeAsync()` gains a step before it calls `DistributedApplicationTestingBuilder.CreateAsync(...)`:

1. Probe `http://localhost:8081` with a short timeout (~1-2s).
2. **If it responds:** treat it as the warm cluster. Set `GatewayHttpsUrl = "http://localhost:8081"` (Playwright navigation target) and `GrpcUrl = "http://localhost:8080"` (native gRPC target), leave `App` as `null`, and return — skip the AppHost boot entirely.
3. **If it doesn't respond (timeout or connection refused):** fall through to exactly today's behavior — fresh `DistributedApplicationTestingBuilder` boot, unique `ClusterId`, full isolation.

`DisposeAsync()` becomes a no-op when `App` is `null` — the fixture must never stop a process it didn't start.

This is purely additive and safe by construction: CI has no warm cluster listening on 8081, so it always takes the fallback path unchanged. A developer who hasn't started a warm cluster also gets unchanged (if slower) behavior — nothing breaks if this feature is never used.

**One concrete wiring detail:** port 8080 is HTTP/2-only cleartext (no TLS), which the fixture's existing code already explains is necessary to avoid ALPN-negotiation-to-HTTP/1.1 failures (`DigitalBrainAppHostFixture.cs:19-25`). A cleartext HTTP/2 *client* requires enabling `Http2UnencryptedSupport` on the channel's handler — this must be scoped to the warm-cluster `GrpcChannel` specifically (e.g. via a dedicated `SocketsHttpHandler`), not process-wide, so it doesn't affect other gRPC clients in the same test process.

### 2.3 Auto-build-if-stale Flutter bundle

Before either path (warm or fresh), check whether `app/build/web` is stale relative to the Flutter source (a content hash or mtime check over `app/lib/**` and `app/pubspec.lock` against a marker file written after the last successful build). If stale or missing, run the build automatically (`flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true`) instead of requiring the developer to remember the command. Since the bundle is served live from disk (§1), a rebuild is picked up by an already-running warm cluster with no restart.

### 2.4 Collapse the env-var ceremony into one VS-runnable category

Today a developer must know and set three separate switches (`RUN_FLUTTER_E2E`, `DIGITALBRAIN_E2E_HEADED`, `FAST_UI_E2E`) to get a headed, fast-timeout render run. Collapse this into one test category/trait that VS Test Explorer can run directly, with sane local defaults (headed when running interactively, headless when running in CI — CI's existing `--filter "FullyQualifiedName!~E2E"` is untouched, so CI behavior does not change).

---

## 3. Dev workflow, before and after

**Before:** edit bundle C# → remember to `cd app && flutter build web ...` if Dart changed → set three env vars → run test → wait 30-120s for a fresh Aspire cluster to boot → watch render → repeat from a cold boot every time.

**After:** start the warm cluster once (`dotnet run --project DigitalBrain.Kernel`, `DIGITALBRAIN_WEBROOT` set) → edit bundle C# → click "Run Test" (Render category) in VS Test Explorer → fixture detects the warm cluster, builds the Flutter bundle only if stale, publishes+installs the edited pack (hot-swaps via the unconditional re-embody in `TryEmbody`) → Playwright renders it in a few seconds → repeat, still attached to the same warm cluster.

---

## 4. Error handling / fallback semantics

- **Warm cluster unreachable:** fixture falls back to a fresh boot. No error surfaced to the developer beyond the normal (slower) render-test run.
- **Warm cluster reachable but in a bad state** (e.g. a previous pack version left it confused): documented escape hatch is to restart the bare-Kernel process — state is in-memory, so this is a clean reset with no storage to purge.
- **Flutter bundle build fails during auto-build:** the test fails with the build's own error output surfaced, same as today's manual-build failure mode — no new silent-failure path introduced.
- **CI:** entirely unaffected. No warm cluster is ever listening in CI, and the fast-suite filter is untouched.

---

## 5. Testing / verification plan

- A new fast unit/integration test asserts the fixture's probe-and-attach branch: given a stub listener on `localhost:8081`, `InitializeAsync()` does not invoke `DistributedApplicationTestingBuilder` and populates the expected URLs.
- A new test asserts the fallback branch: given nothing listening, behavior is unchanged from today (existing E2E tests already cover this path; add an explicit assertion that `App` is non-null in that case).
- Manual verification pass (this is inherently a DX change): start a warm cluster, edit `StarterBundleSource.cs`, re-run `StarterBundleRendersE2ETests` from VS Test Explorer twice in a row, confirm the second run completes in low single-digit seconds rather than tens of seconds, and confirm the rendered output reflects the edit.
- `dotnet build` + full fast suite (`--filter "FullyQualifiedName!~E2E"`) green, as required by every change in this repo.

---

## 6. Non-goals (deferred, tracked as follow-ups)

- **Flutter-side auto-refresh for a human manually browsing the app** (the `SurfaceDemoScreen` "click Run demo" flow, `app/lib/features/surface_demo/surface_demo_screen.dart`). This is a separate interactive-exploration UX gap; the automated Playwright render test already navigates and asserts programmatically without needing that button, so it does not block this loop.
- **AI-generation-from-spec wired into the authoring loop** (`CodeFoundryClosedLoopNeuron` / `run_code_foundry` / `run_closed_loop` MCP tools). These exist as disconnected pieces today; wiring them into a TDD-style "describe it, AI generates the bundle, iterate against this same tightened loop until green" flow is real future work, but per Musk's algorithm we accelerate the existing process before automating a new one on top of it. Tracked as the next roadmap item after this ships.
- **A persisted trust/incidental bug found during research:** `FilterMarketplace` is missing from `JournalJsonContext`, currently failing a CI test. Unrelated to this spec; should be fixed as its own small, separate change.

---

## 7. Phased build decomposition

1. **Slice 1 — Auto-build-if-stale Flutter bundle.** Content-hash/mtime check + auto-invoke `flutter build web`. Self-contained, no fixture changes.
2. **Slice 2 — Collapse env-var ceremony.** One Render test category, VS-Test-Explorer-runnable, sane local-vs-CI defaults. No behavior change to what actually runs, only to how it's invoked.
3. **Slice 3 — Warm-cluster attach-with-fallback.** The `DigitalBrainAppHostFixture` probe/attach/fallback change, the `Http2UnencryptedSupport` wiring for the warm-cluster gRPC channel, and the documented `dotnet run --project DigitalBrain.Kernel` startup instructions.

Each slice is independently shippable and independently testable; Slice 3 delivers the actual cycle-time win and depends on nothing from Slices 1-2 (they can ship in any order, but 1-2 are smaller and lower-risk, so they go first).

---

## 8. Success criteria

- A developer can edit a bundle's C#, re-run its render test from VS Test Explorer, and see it render in a few seconds — not 30-120 seconds — without manually running any shell command or setting any environment variable beyond starting the warm cluster once per session.
- CI behavior (fast suite green, E2E suite filtered out) is completely unchanged.
- No existing test's behavior changes when a warm cluster is not running.
