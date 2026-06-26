# Bucket D — Flutter Render E2E Design

**Goal:** Prove the full server-driven UI path end-to-end in a real browser: a real embodied pack emits a surface, and a Playwright-driven Chromium loads the live Flutter web app and asserts that the specific pack-driven widget actually rendered. Replace the current half-real, self-skipping `PackEmbodimentRendersE2ETests` stub with a deterministic, gated, green test.

## Background

The server-side path already works and is reachable from a .NET test client over native gRPC: `pack → install → ALC embody → ExperienceUsed → UiSurface → UiSurfaceRfwBridge → RfwCard → HomeFeedBus → WatchHomeFeed`. The Flutter client already consumes that stream (`living_canvas_screen.dart`), renders `RfwCard`s through the RFW runtime, and has a complete web scaffold + a web-build CI.

What is missing for a *browser* render E2E (mapped during brainstorming):
- The kernel speaks **native gRPC over HTTP/2 only** — a browser cannot reach `WatchHomeFeed` without **gRPC-Web**.
- **Nothing serves the Flutter web bundle** from the cluster (no static files; `AddFlutterClient` is desktop-only).
- RFW-rendered widgets carry **no stable identifier** a browser test can assert on (Flutter web paints to `<canvas>`; per-widget DOM exists only via the semantics/accessibility tree).
- The existing E2E fixture **hangs** on `WaitForResourceHealthyAsync("silo")` — a stale name from the Silo→Kernel rename (the resource is `"kernel"`). Because the E2E collection is not `[Trait("Category","E2E")]`-tagged, the skipped test is still *selected*, instantiates the fixture, and the hang took down the Bucket A high-sev run.

## Design decisions (locked during brainstorming)

1. **Scope:** Full browser render E2E — build the real gaps, not just un-skip.
2. **Origin model:** **Same-origin via the kernel.** The kernel's Kestrel serves the built Flutter web bundle *and* gRPC-Web on the same origin. The browser loads the SPA from the kernel and gRPC-Webs back to that same origin. This **drops the CORS gap and `kernel_port.txt`** — the browser's `Uri.base` *is* the kernel, which `app/lib/grpc/endpoint.dart` already falls back to.
3. **Render assertion:** **Flutter semantics + stable identifier.** The RFW host wraps each rendered surface root in `Semantics(identifier: <stableId>)`; Flutter web exposes this as a DOM attribute on a `flt-semantics` node. Playwright waits for that node. Semantics is force-enabled at boot for the test build only.
4. **Web build source:** **Pre-built prerequisite + skip-if-absent.** A documented step / CI stage runs `flutter build web` into `app/build/web`; the fixture points the kernel's static root there. Absent build ⇒ the test **skips** with an actionable message — never hangs, never silently passes.
5. **Gating:** **`[Trait("Category","E2E")]` + opt-in + prereq skip.** Tagging fixes the Bucket A filter leak; the test additionally skips unless `RUN_FLUTTER_E2E=true` and `app/build/web/index.html` exists. Runs only via an explicit `--filter Category=E2E` invocation or a dedicated CI E2E stage.

These collapse the original 6 gaps to **4 real ones**: gRPC-Web, static serving, RFW semantics identifiers, and the gated test.

## Architecture

Single-origin render path:

```
real pack → install → ALC embody → ExperienceUsed → UiSurface
   → UiSurfaceRfwBridge → RfwCard → HomeFeedBus
   → WatchHomeFeed (gRPC-Web, same origin as the SPA)
   → Flutter app (RFW runtime) → render → Semantics(identifier)
   → flt-semantics DOM node → Playwright assertion + screenshot
```

The kernel becomes the single browser-facing origin for the test: it serves `index.html` + Flutter assets over HTTP/1.1 and answers gRPC-Web on the same HTTP/1+HTTP/2 endpoint. gRPC-Web rides HTTP/1.1, so it coexists with static file serving on one endpoint.

## Components & changes

### Phase 1 — Backend hosting (`brain/`, no Flutter/browser needed to verify)

- **gRPC-Web on the kernel.** Add `Grpc.AspNetCore.Web` (central version in `Directory.Packages.props`). Enable gRPC-Web for the existing `WatchHomeFeed`/UiGateway services on the kernel's HTTP/1+HTTP/2 endpoint (`app.UseGrpcWeb(...)` + per-service enablement). **Additive:** native gRPC over HTTP/2 (existing .NET clients and `GatewayGrpcWireTests`) is unaffected.
- **Static file serving on the kernel.** `UseStaticFiles()` + SPA fallback `MapFallbackToFile("index.html")`, rooted at a configurable web root read from `DIGITALBRAIN_WEBROOT`. **Only active when that env var is set** → normal/production kernel boots are unchanged (no web root ⇒ no static middleware, no fallback).
- Same-origin means **no CORS** and **no `kernel_port.txt`** handler.

**Phase 1 verification:** a .NET integration test (or the E2E fixture's Phase-1 slice) asserts (a) a gRPC-Web `WatchHomeFeed` call over HTTP/1.1 connects, and (b) `GET /` returns `index.html` when `DIGITALBRAIN_WEBROOT` points at a folder containing it.

### Phase 2 — App testability (`app/`)

- **Semantics identifier on rendered surfaces.** In the RFW host / surface widget (`lib/rfw_host/rfw_surface.dart` / `rfw_runtime_host.dart`), wrap each rendered surface root in `Semantics(identifier: <stableId>)`. `stableId` derives deterministically from the card (its `rootWidget` plus a marker field carried in `dataJson`). Flutter web renders this as a stable DOM attribute on the corresponding `flt-semantics` node.
- **Force-enable semantics for the test build.** Flutter web only builds the semantics DOM when accessibility is requested. At boot, call `ensureSemantics()` when `--dart-define=DIGITALBRAIN_E2E=true` is set. The normal production build (`flutter build web --release` without the define) is unaffected.
- **No endpoint change.** Same-origin resolution is already handled by `endpoint.dart` (web fallback to `Uri.base.host:Uri.base.port`).

**Phase 2 verification:** a Flutter widget/semantics test (run via the Dart/Flutter MCP) asserts that rendering a representative `RfwCard` produces a node carrying the expected semantics identifier.

### Phase 3 — Fixture + gating + render test (`brain/DigitalBrain.Tests/E2E`)

- **Fix the fixture** (`DigitalBrainAppHostFixture.cs`): `WaitForResourceHealthyAsync("silo")` → `"kernel"`, with a **bounded timeout** (CancellationToken) so a future rename surfaces as a fast failure, not a 5-minute hang.
- **Tag the collection** `[Trait("Category","E2E")]` on `DigitalBrainE2ECollection` (or the test classes) so `Category!=E2E` cleanly excludes the whole E2E surface from the default high-sev run.
- **Wire the bundle:** the fixture sets the kernel resource's `DIGITALBRAIN_WEBROOT` env to the absolute `app/build/web` path before building the AppHost.
- **Gating / skip:** before booting heavy resources, the test skips (xUnit v3 dynamic `Assert.Skip`) unless `RUN_FLUTTER_E2E=true` **and** `app/build/web/index.html` exists, with a message naming the remediation (`flutter build web --release --dart-define=DIGITALBRAIN_E2E=true`).
- **Real render proof:** publish + install a real `IPackBehavior` pack whose embodiment emits a **renderable** surface — one that passes the app's `_isRenderableSurface` filter (non-empty `dataJson` with an RFW `source` / ui-layout tree, not a `synapse-broadcast`) and carries a unique marker. Then:
  1. (Optional backend checkpoint) the .NET client asserts the matching `RfwCard` arrives on the `WatchHomeFeed` stream.
  2. Playwright navigates to the kernel's HTTP URL, waits for `flt-semantics-identifier=<marker>` (bounded timeout), asserts it is present, and screenshots for the artifact.

**Phase 3 verification:** `dotnet test --filter "Category=E2E" ` with `RUN_FLUTTER_E2E=true` and a present web build → the render test passes; with the flag/build absent → it skips cleanly; the default high-sev run (`Category!=E2E`) never selects it.

## Error handling & isolation

Three independent off-switches guarantee nothing leaks into default runs:
- Static serving is gated on `DIGITALBRAIN_WEBROOT` (unset in normal/prod ⇒ inert).
- Semantics is gated on the `DIGITALBRAIN_E2E` dart-define (absent in prod build ⇒ inert).
- The E2E test is gated on `[Trait Category=E2E]` + `RUN_FLUTTER_E2E` + prereq presence.

Additionally: the health-wait is bounded; missing prerequisites produce an honest skip with remediation text; gRPC-Web is purely additive to native gRPC.

## Testing strategy

- **Phase 1:** .NET integration test — gRPC-Web call + static `GET /`.
- **Phase 2:** Flutter semantics widget test (Dart/Flutter MCP).
- **Phase 3:** the gated Playwright render E2E.

Each phase is independently verifiable, so the work lands incrementally and a failure localizes to one layer.

## Constraints & conventions

- **Context7 / Flutter MCP first** for all framework APIs before writing code: `Grpc.AspNetCore.Web` (`UseGrpcWeb`, per-service enablement), ASP.NET Core static files / SPA fallback, xUnit v3 dynamic skip (`Assert.Skip`), Flutter `Semantics(identifier:)` + `ensureSemantics()` + the `flt-semantics-identifier` attribute, Playwright .NET selectors/waits. The *mechanisms* in this spec are fixed; only exact API spellings get confirmed.
- net11.0; central package versions only (`Directory.Packages.props`); no `Version="*"`.
- No vacuous `///` XML docs; self-explanatory names; relative paths; small inline comments only where non-obvious.
- `brain/` and `app/` are separate git repos; changes in each are committed in their own repo. (Phase 2 lands in `app/`; Phases 1 and 3 in `brain/`.)

## Out of scope (deferred)

- **Production cross-origin path** (GitHub Pages app ↔ `api.digitalbrain.tech` kernel, with CORS): the same-origin design here does not exercise it. If wanted, it is a follow-on (gRPC-Web CORS + a cross-origin Playwright run).
- A dedicated separate E2E test *project* (stronger isolation than traits) — not needed given the trait + opt-in gating.
- `AddFlutterClient` web *running* resource — superseded by the kernel serving the pre-built bundle.
- Visual/screenshot baseline diffing — the semantics-identifier assertion is the correctness signal; the screenshot is only a diagnostic artifact.

## Self-review

- **Placeholders:** none. The two API-spelling notes (`ensureSemantics`, `flt-semantics-identifier`) are explicit, with verification assigned to the planning/implementation step — not gaps.
- **Consistency:** the origin model (same-origin) is applied consistently across CORS-drop, `kernel_port.txt`-drop, fixture webroot wiring, and the endpoint-no-change note.
- **Scope:** sized for one implementation plan with three sequential, independently-verifiable phases.
- **Ambiguity:** "renderable surface" is pinned to the app's existing `_isRenderableSurface` filter; "stable identifier" is pinned to `rootWidget` + a `dataJson` marker.
