# Experiences as First-Class, Shell-Rendered, Smallest-Testable Units (Design)

Date: 2026-06-28
Status: Proposed — awaiting review
Scope: `brain/` (canonical backend) + `app/` (Flutter client)
Prerequisite: `2026-06-28-domains-experiences-travel-slice-design.md` and the 2026-06-28 "Task D"
CONTINUITY entry (the travel browser E2E now passes end-to-end up to Flutter on master, commit `e2db26a`).

## 1. Goal

An Experience is a guided, multi-hop journey, but today it has **no first-class full-screen
rendering**. Live pack surfaces only appear as cascading floating panels on the canvas. This
design gives an Experience a coherent full-screen home and makes each Experience an
independently testable unit with cheap, observable browser E2Es.

Three goals:

1. **Render experiences coherently.** An Experience renders as a full-screen journey that
   advances in place (each hop replaces the prior), not as random floating panels.
2. **Every experience = smallest testable piece.** Per-hop unit tests over the pack
   (already provided by `TravelPackTests`) plus a thin browser E2E so authoring a new
   experience's E2E is ~10 lines.
3. **View/drive the Flutter UI from the E2E.** Headed/headless toggle, per-hop screenshots,
   and on-failure browser diagnostics so a failing hop is diagnosable without re-deriving the
   whole pipeline.

## 2. Baseline (confirmed in code on master `e2db26a`)

- **Canvas path** (`/#/canvas` → `app/lib/features/canvas/panel/floating_panel_layer.dart`):
  every `WatchHomeFeed` card becomes a draggable floating panel,
  `semanticsId == panel.id == correlationId == surfaceId`. Travel hops land here today as
  cascading floaters; **the merged browser E2E drives this path**.
- **Shell path** (`/` → `app/lib/shell/forui_app_shell.dart`, `_onCard` ~153–183): a surface
  is kept only if it looks like a shell tree (`app-shell`/`widget-tree`/`shell`/`activeContent`)
  **or** has a non-empty `kind` (→ `_surfacesByKind[kind]`). Travel hops are
  `UiSurface.ForRfw` with **no `kind`**, so they fall through both branches and are **silently
  dropped**. This is the rendering gap.
- **The rendering primitive already exists**: `_renderEnvelope` (~222–263) renders the
  inline-RFW `source` case keyed by `correlationId`. The shell simply never chooses to mount a
  hop.
- **Marker plumbing is free for RFW surfaces**: `UiSurfaceRfwBridge.FromUiSurface`
  (`brain/DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs`) early-returns for `ForRfw`/`source`
  surfaces, serializes `surface.Props` into `dataJson`, and already prefers `props[surfaceId]`
  for `CorrelationId`. Any prop a pack sets on a hop flows straight to Flutter's `_decode`
  untouched — **no bridge change needed**.

**Key de-risking fact:** the merged E2E uses the **canvas**, not the shell. Adding experience
rendering on a new route (or to the shell) regresses neither the canvas nor the merged E2E.
The design choice is about UX coherence and testability, not regression risk.

## 3. Architecture

### 3.1 Backend — the `activeExperience` hop marker (additive, `brain/`)

Each hop carries a marker so any host can recognize it as part of a live experience and pick
the active one. Add a helper on the surface API:

```csharp
// DigitalBrain.Core — alongside UiSurface.ForRfw
public static UiSurface ForExperienceHop(
    string experienceRef,   // "<pack>/<experienceId>", e.g. "travel/plan-trip"
    string surfaceId,       // per-hop id, e.g. "travel-hotels"
    string libraryName,
    string rootWidget,
    string dataJson,
    string source);
```

It produces the existing `ForRfw` shape plus props:

```
activeExperience = "<pack>/<experienceId>"
experienceId     = "<experienceId>"
surfaceId        = "<surfaceId>"
```

- **No bridge changes.** The RFW early-return path already serializes `surface.Props` into
  `dataJson` and already uses `props[surfaceId]` as `CorrelationId`, so the marker and the
  per-hop semantics id both ride along for free.
- **`TravelPack`** (`brain/DigitalBrain.Tests/E2E/Packs/TravelPack.cs`) is updated to emit its
  five hops through `ForExperienceHop`. The five hop `surfaceId`s are exactly those the existing
  E2E asserts: `travel-intro`, `travel-hotels`, `travel-events`, `travel-activities`,
  `travel-summary`.
- **`TravelPackTests`** extends its per-hop assertions to confirm `activeExperience` is present
  and correct on each emitted surface (the smallest-testable-unit guarantee at the unit level).

### 3.2 App — dedicated experience-host route (`app/`)

A new, thin host owns full-screen experience rendering. Shell and canvas are untouched.

- **Route:** `/#/experience` and `/#/experience/:pack/:experienceId` (direct targeting). Added
  to the existing GoRouter config.
- **`ExperienceHostScreen`** (new, `app/lib/features/experience/experience_host_screen.dart`):
  - Subscribes to `WatchHomeFeed` using the same channel/client setup as `ForuiAppShell`.
  - On each card: `_decode` the `dataJson`; if it has `activeExperience` (matching the route's
    target when one is supplied), set it as `_activeHop` and `setState`.
  - Renders **only** `_activeHop`, full-screen, via the shared RFW renderer. A new hop envelope
    **replaces** the previous one in place — that is the journey advancing.
  - Chrome-free except a minimal back/exit affordance that pops to `/`.
- **Reuse, not duplication:** extract the inline-RFW rendering branch from
  `ForuiAppShell._renderEnvelope` (the `source`/`correlationId` path) into a shared
  helper/widget (e.g. `app/lib/rfw_host/rfw_surface_view.dart`) used by both the shell and the
  experience host. `RfwRuntimeHost`, the `_decode` helper, and the `_handleSurfaceEvent`
  shape are shared so there is one renderer, not two.
- **No regression:** `ForuiAppShell` (`/`) and `FloatingPanelLayer` (canvas) are not modified
  except for the mechanical extraction of the shared renderer (behavior-preserving). The merged
  canvas E2E continues to pass.

### 3.3 Data flow (one hop)

```
Flutter tap on a card (or driver SendExperienceStepAsync)
  → ExperienceStep synapse
  → GeneratedNeuron[generated-travel].Handle → travel IPackBehavior (stateful)
  → returns UiSurface.ForExperienceHop(experienceRef="travel/plan-trip",
                                       surfaceId="travel-hotels", ...)
  → UiSurfaceRfwBridge → HomeFeedBus → WatchHomeFeed (gRPC-Web)
  → ExperienceHostScreen sees activeExperience → replaces _activeHop
  → shared RFW renderer paints it full-screen (flt-semantics-identifier = surfaceId)
```

The entry hop is the same minus the tap (triggered by the `start` step / `ExperienceUsed`).

### 3.4 Test — `ExperienceFlowDriver` (standalone, over `DigitalBrainBrowserFixture`)

A standalone driver composed over the existing fixture (not a base class — idiomatic with the
current xUnit collection-fixture model). It generalizes the publish/sign/install/navigate/
wait-for-feed/tap/assert/screenshot boilerplate currently inline in
`TravelPlanTripRendersE2ETests` and `DigitalBrainAppHostFixture`.

```csharp
var driver = new ExperienceFlowDriver(fixture, pack: "travel", experienceId: "plan-trip");
await driver.PublishAndInstallAsync(TravelPackSource.Read(), description: "...");
await driver.OpenAsync();                                  // nav + wait-for-WatchHomeFeed
await driver.TriggerExperienceAsync(("prompt", "plan a trip to Bali next month"));
await driver.AssertHopRendersAsync("travel-intro");
await driver.TapAsync("flight.selected", ("flightId", "FL-001"));
await driver.AssertHopRendersAsync("travel-hotels");
// ...continue through summary
```

API:

- `PublishAndInstallAsync(code, description, …)` — wraps the ECDSA self-sign + publish +
  install (currently in the fixture; the driver composes the fixture's existing
  `PublishPackAsync`/`InstallPackAsync`).
- `OpenAsync()` — navigates to `/#/experience/<pack>/<experienceId>` and
  `RunAndWaitForResponseAsync` on `WatchHomeFeed` (subscribe-before-emit, because `HomeFeedBus`
  has no replay and dedups identical re-sends).
- `TriggerExperienceAsync(args)` / `TapAsync(eventName, args)` — fire `ExperienceStep` via the
  fixture's `SendExperienceStepAsync`.
- `AssertHopRendersAsync(surfaceId)` — waits for `[flt-semantics-identifier="<surfaceId>"]`,
  asserts count == 1, screenshots into `AppContext.BaseDirectory/e2e-screenshots`.

**Diagnostics (goal 3):**

- Headed/headless **env toggle** (e.g. `DIGITALBRAIN_E2E_HEADED`), with `SlowMo` when headed,
  so a developer can watch the journey drive itself.
- Per-hop screenshots into `AppContext.BaseDirectory/e2e-screenshots` (collectable artifacts,
  not the user temp).
- **On-failure capture**: browser console logs, page errors (`page.on("console")` /
  `page.on("pageerror")`), and a DOM / `flt-semantics` dump written to the artifacts dir, so a
  failing hop is diagnosable from artifacts alone.
- **Separate documented dart-MCP recipe** (in the driver's doc comment / a short README): for
  deep manual debugging, launch a **debug `flutter run`** against the same kernel and use
  `get_widget_tree` / `get_runtime_errors`. This is intentionally NOT wired into the automated
  E2E: the automated path uses a `flutter build web --release` static bundle driven by
  Playwright, which has no Dart Tooling Daemon (DTD) for the dart MCP to attach to.

**Migration:** `TravelPlanTripRendersE2ETests` is rewritten onto the driver (~10 lines) as both
the proof of ergonomics and the regression anchor. It now targets the experience route, not
`/#/canvas`.

## 4. The web bundle (unchanged invariant)

The browser E2E still requires the bundle built as
`flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true`
(`--no-tree-shake-icons` is mandatory; `main.dart` forces semantics under `DIGITALBRAIN_E2E`).
The new route must be present in that built bundle, so the bundle is rebuilt before the E2E runs.

## 5. Explicitly NOT doing (YAGNI)

- No shell "Plan a trip" entry button — the experience host is reached by route/deep-link this
  phase; a real shell nav entry is a follow-up.
- No experience rendering inside the shell chrome — the host is chrome-free.
- No canvas "focused mode" — the canvas keeps its free-form floating panels.
- No per-user (vs per-install) flow state.
- No `NeuroPack → Domain` rename.
- No cross-origin (prod GitHub Pages ↔ remote kernel) E2E.
- No dart-MCP integration into the automated E2E flow.

## 6. Testing & verification

- **Baseline first:** as step 1 on the feature branch, run the existing travel browser E2E once
  against master behavior to confirm it is green **before** changing anything.
- **Inner loop:** `dotnet build` + `TravelPackTests` (extended for the marker) + any touched
  unit tests.
- **App:** `flutter analyze` clean; rebuild the web bundle with the new route.
- **E2E:** the rewritten `TravelPlanTripRendersE2ETests` (gated by `RUN_FLUTTER_E2E` + prebuilt
  bundle), now driving the experience route.
- **Ritual after changes:** `dotnet build`, relevant `dotnet test`, `aspire doctor` (per
  `brain/AGENTS.md`).
- **Context7** for any Playwright / Aspire.Hosting.Testing / go_router / RFW API touched.
- **Code review** (opus) before returning.

## 7. Open risks to confirm during planning

1. The exact GoRouter registration shape for `/#/experience/:pack/:experienceId` and how
   `ExperienceHostScreen` reads the path params (web hash strategy is already in use).
2. That extracting the shared RFW renderer from `ForuiAppShell._renderEnvelope` is truly
   behavior-preserving for the shell's existing surface paths (verify the shell still renders
   marketplace/installed/login fallbacks unchanged).
3. That `ForExperienceHop` props survive the bridge → `dataJson` round-trip exactly (a unit
   test over `UiSurfaceRfwBridge.FromUiSurface` confirms `activeExperience`/`surfaceId` land in
   the emitted `dataJson` and `CorrelationId`).
4. Whether the host should match on `activeExperience` value when a route target is supplied,
   or render any experience hop when no target is given — pick one explicitly (default: match
   when the route supplies `:pack/:experienceId`, else render any experience hop).
