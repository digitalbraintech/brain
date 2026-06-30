# Authoring a Bundle

A bundle is a `NeuroPack` + manifest (see `docs/specs/2026-07-01-distribution-and-bundles.md`).
This is the test-first loop for building one. **Write the test first, then the bundle.**

## The single source of truth

A bundle is defined **once**, as its embodiable pack source string (a `KitExperience`
subclass). Both the fast in-memory test and the live-render E2E compile that same string —
never re-type the experience as a second C# type.

The copy-me starter lives at `DigitalBrain.Tests/Authoring/StarterBundleSource.cs`.

## Two speeds

### 1. Fast loop (milliseconds, no browser) — your daily loop

`BundleHarness` compiles your bundle's source in-process (the same Roslyn/ALC path the
kernel uses) and drives `ExperienceStep`s. Assert the emitted `UiWidgetTree` with the
`UiTreeAssertions` matchers (`ShouldHaveNodeOfType`, `ShouldHaveButtonWithLabel`,
`ShouldHaveSelect`, `ShouldContainText`, `ToGoldenSnapshot`, …).

```csharp
using var harness = new BundleHarness(MyBundleSource.Code, pack: "my-bundle", experienceId: "my-bundle");
var tree = harness.GetTree("ask");
tree.ShouldHaveButtonWithLabel("Go");
```

Run only your bundle's fast tests:

```sh
cd brain
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MyBundleTests"
```

### 2. Render loop (tens of seconds, real Flutter) — run before publishing

`LiveRenderVerifier` publishes + installs your bundle into the full Aspire stack and drives
the real Flutter renderer, asserting surfaces via Flutter Semantics and capturing screenshots.

Prerequisites (gated by `E2EPrerequisites.RequireRenderE2E()` so it skips unless you opt in):

```sh
# 1. Build the Flutter web bundle once (non-constant IconData needs --no-tree-shake-icons):
cd app
flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true

# 2. Run the render E2E with the opt-in flag:
cd ../brain
RUN_FLUTTER_E2E=true dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MyBundleRendersE2ETests"
```

Useful env flags:

- `RUN_FLUTTER_E2E=true` — opt in to the render E2E (otherwise skipped).
- `DIGITALBRAIN_E2E_HEADED=true` — watch the browser render live as the test runs.
- `DIGITALBRAIN_E2E_SLOWMO=500` — slow Playwright actions (ms) so you can see each step.
- `FAST_UI_E2E=1` — shorter assertion timeouts for a quicker render pass.
- `DIGITALBRAIN_E2E_REPLICAS=1` — kernel replicas for the test stack (default 1).

While iterating visually you can also attach the dart MCP tools (`get_widget_tree`,
`hot_reload`) to a running debug Flutter app.

## Write a new bundle in ~15 minutes

1. Copy `DigitalBrain.Tests/Authoring/StarterBundleSource.cs`; rename the type, `Pack`,
   `ExperienceId`, and hops.
2. Copy `DigitalBrain.Tests/Authoring/StarterBundleTests.cs`; write the failing fast test
   for your entry hop.
3. Edit your bundle source until the fast test is green.
4. Copy `DigitalBrain.Tests/E2E/StarterBundleRendersE2ETests.cs`; run it with
   `RUN_FLUTTER_E2E=true` (and `DIGITALBRAIN_E2E_HEADED=true`) to watch it render.
5. When both are green, the bundle is publishable.
