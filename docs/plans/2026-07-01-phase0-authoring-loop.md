# Phase 0 — Blessed Bundle-Authoring Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make authoring a new content bundle a fast, single-source-of-truth, test-first loop — define the bundle once (as embodiable pack source), drive it in-memory in milliseconds, and render it live on demand, all from `dotnet test`.

**Architecture:** Today an experience is authored twice — once as the embodiable source string (`MarketplaceSeeds.HelloWorldPackCode`) and again as a duplicate inline `KitExperience` type in the fast harness test. This plan adds a `BundleHarness` that compiles the *shipped* pack source in-process via the existing `PackAlcEmbodier`, so the fast model loop and the live-render E2E consume the **same** source. It then ships a copy-me starter bundle (fast test + skippable render test) and the authoring docs.

**Tech Stack:** .NET 11 (net11.0), xUnit, Reqnroll (existing), Orleans TestCluster (existing), Roslyn-based `PackAlcEmbodier` (existing, `DigitalBrain.Kernel.Foundry`), Playwright via `DigitalBrainBrowserFixture` (existing), `Aspire.Hosting.Testing` (existing).

## Global Constraints

- Target framework is **net11.0**; never pin `Version="*"`; package versions are central in `Directory.Packages.props` and updated deliberately.
- **No vacuous `/// <summary>`** that restates a signature. Self-explanatory names over comments; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done** — evidence before assertions.
- Look up any unfamiliar library/framework API via **Context7** before writing code against it.
- All work is in the `brain` repo (`digitalbraintech/framework`), on branch `spec/distribution-and-bundles` (already checked out). Use relative paths; never leak user-profile paths.
- Packs are signed by default; the install gate may reject unsigned packs. The E2E fixture (`DigitalBrainBrowserFixture.PublishPackAsync`) already self-signs — do not bypass signing.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

- **Create** `DigitalBrain.Tests/Ui/BundleHarness.cs` — fast, in-memory harness that embodies a bundle's pack source string via `PackAlcEmbodier` and drives `ExperienceStep`s, returning the emitted `UiSurface` / `UiWidgetTree`. Reuses the existing `UiTreeAssertions`. One responsibility: drive a bundle's *shipped source* in-memory.
- **Create** `DigitalBrain.Tests/Ui/BundleHarnessTests.cs` — proves `BundleHarness` drives the shipped `HelloWorldPackCode` and yields the same tree the duplicate inline type used to.
- **Modify** `DigitalBrain.Tests/Ui/SimpleColorPickerHarnessTests.cs` — remove the duplicate inline `HelloWorldExperience` type; drive the shipped source via `BundleHarness`.
- **Create** `DigitalBrain.Tests/Authoring/StarterBundleSource.cs` — the copy-me starter bundle: pack source string + pack/experience ids + hop-name constants.
- **Create** `DigitalBrain.Tests/Authoring/StarterBundleTests.cs` — fast `BundleHarness` test for the starter (the "empty file → green test" reference).
- **Create** `DigitalBrain.Tests/E2E/StarterBundleRendersE2ETests.cs` — skippable live-render E2E for the starter (the "live-rendered UI in one `dotnet test`" reference).
- **Create** `brain/docs/authoring-a-bundle.md` — the blessed loop, two speeds, exact commands, env flags, copy-me pointer.
- **Modify** `brain/AGENTS.md` — one-line pointer to the authoring doc.

---

## Task 1: `BundleHarness` — drive the shipped pack source in-memory

**Files:**
- Create: `DigitalBrain.Tests/Ui/BundleHarness.cs`
- Test: `DigitalBrain.Tests/Ui/BundleHarnessTests.cs`

**Interfaces:**
- Consumes: `DigitalBrain.Kernel.Foundry.PackAlcEmbodier` — `new PackAlcEmbodier()`, `EmbodiedPack Embody(string packName, string code)`; `EmbodiedPack` exposes `IReadOnlyList<Synapse> Handle(Synapse)` and `void Dispose()`. `DigitalBrain.Core.ExperienceStep(string Pack, string ExperienceId, string EventName, IReadOnlyDictionary<string,string> Args)`. `DigitalBrain.Core.UiSurface` with `Props["tree"]` of type `UiWidgetTree`. Existing `DigitalBrain.Tests.Ui.UiTreeAssertions` extension methods.
- Produces: `DigitalBrain.Tests.Ui.BundleHarness` — `BundleHarness(string packCode, string pack, string experienceId)`, `UiSurface Trigger(string eventName, params (string key,string value)[] args)`, `UiWidgetTree GetTree(string eventName, params (string key,string value)[] args)`, `IDisposable`.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Ui/BundleHarnessTests.cs`:

```csharp
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class BundleHarnessTests
{
    [Fact]
    public void Drives_shipped_hello_world_source_and_asserts_ask_hop_tree()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var tree = harness.GetTree("ask");

        tree.ShouldHaveNodeOfType(DigitalBrain.Core.Ui.TextField);
        tree.ShouldHaveButtonWithLabel("Greet");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleHarnessTests"`
Expected: FAIL — compile error, `BundleHarness` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `DigitalBrain.Tests/Ui/BundleHarness.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;

namespace DigitalBrain.Tests.Ui;

// Compiles a bundle's SHIPPED pack source in-process (same Roslyn/ALC path the kernel uses)
// and drives ExperienceSteps against it, so the fast model loop and the live-render E2E
// consume one source of truth. No browser, no Aspire.
public sealed class BundleHarness : IDisposable
{
    private readonly EmbodiedPack _pack;
    private readonly string _experiencePack;
    private readonly string _experienceId;

    public BundleHarness(string packCode, string pack, string experienceId)
    {
        _pack = new PackAlcEmbodier().Embody(pack, packCode);
        _experiencePack = pack;
        _experienceId = experienceId;
    }

    public UiSurface Trigger(string eventName, params (string key, string value)[] args)
    {
        var step = new ExperienceStep(
            _experiencePack, _experienceId, eventName,
            args.ToDictionary(a => a.key, a => a.value));

        var outputs = _pack.Handle(step);
        return outputs.OfType<UiSurface>().FirstOrDefault()
               ?? throw new InvalidOperationException($"No UiSurface emitted for step '{eventName}'.");
    }

    public UiWidgetTree GetTree(string eventName, params (string key, string value)[] args)
    {
        var surface = Trigger(eventName, args);
        if (surface.Props.TryGetValue("tree", out var t) && t is UiWidgetTree tree)
            return tree;

        throw new NotSupportedException($"Step '{eventName}' did not produce a widget tree.");
    }

    public void Dispose() => _pack.Dispose();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleHarnessTests"`
Expected: PASS (1 passed).
- If `EmbodiedPack.Handle`/`Embody` signatures differ, reconcile against `DigitalBrain.Tests/Foundry/PackAlcEmbodierTests.cs` (same API).
- If `Trigger` throws "No UiSurface emitted", the `KitExperience` is filtering on the `ExperienceStep`'s `Pack`/`ExperienceId`. Check how `KitExperience.Handle` matches (read `DigitalBrain.Core` UiKit `KitExperience`) and pass matching ctor values — the declared experience id in `HelloWorldPackCode` is `"hello-world"`, which is what the test uses.

- [ ] **Step 5: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Tests/Ui/BundleHarness.cs DigitalBrain.Tests/Ui/BundleHarnessTests.cs
git commit -m "$(cat <<'EOF'
test: add BundleHarness that drives shipped pack source in-memory

Single-source-of-truth fast loop: embody the bundle's pack source via
PackAlcEmbodier and drive ExperienceSteps, no browser/Aspire.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Remove the duplicate inline experience type

**Files:**
- Modify: `DigitalBrain.Tests/Ui/SimpleColorPickerHarnessTests.cs`

**Interfaces:**
- Consumes: `DigitalBrain.Tests.Ui.BundleHarness` (Task 1), `DigitalBrain.Core.MarketplaceSeeds.HelloWorldPackCode`.
- Produces: nothing new — proves the fast example uses the shipped source.

- [ ] **Step 1: Replace the file contents**

The class currently defines a private `HelloWorldExperience : KitExperience` duplicating `MarketplaceSeeds.HelloWorldPackCode`. Replace the whole file with the version below, which drives the shipped source via `BundleHarness` and deletes the duplicate type:

```csharp
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Ui;

// Fast, in-memory tests over the SHIPPED bundle source (single source of truth) via BundleHarness.
// No browser, no full stack — millisecond feedback. This is the primary daily authoring loop.
public class UiTestingFrameworkExamples
{
    [Fact]
    public void Can_drive_hello_world_experience_and_assert_tree()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var tree = harness.GetTree("ask");

        tree.ShouldHaveNodeOfType(DigitalBrain.Core.Ui.TextField);
        tree.ShouldHaveButtonWithLabel("Greet");
    }

    [Fact]
    public void Can_use_golden_snapshot_and_matchers()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var tree = harness.GetTree("ask");

        var snapshot = tree.ToGoldenSnapshot();
        Assert.Contains("ui:TextField", snapshot);

        tree.ShouldHaveButtonWithLabel("Greet");
    }
}
```

- [ ] **Step 2: Run the tests to verify they pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~UiTestingFrameworkExamples"`
Expected: PASS (2 passed). If `ToGoldenSnapshot` emits a different node-type spelling than `ui:TextField`, adjust the `Assert.Contains` value to match the actual snapshot (run once and read the output).

- [ ] **Step 3: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Tests/Ui/SimpleColorPickerHarnessTests.cs
git commit -m "$(cat <<'EOF'
test: drive fast hello-world examples from shipped source

Delete the duplicate inline KitExperience; consume MarketplaceSeeds
.HelloWorldPackCode via BundleHarness so there is one source of truth.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Copy-me starter bundle (fast test + skippable render test)

**Files:**
- Create: `DigitalBrain.Tests/Authoring/StarterBundleSource.cs`
- Create: `DigitalBrain.Tests/Authoring/StarterBundleTests.cs`
- Create: `DigitalBrain.Tests/E2E/StarterBundleRendersE2ETests.cs`

**Interfaces:**
- Consumes: `BundleHarness` (Task 1); `DigitalBrain.Tests.E2E.LiveRenderVerifier`, `DigitalBrainBrowserFixture`, `DigitalBrainE2ECollection`, `E2EPrerequisites.RequireRenderE2E()` (all existing).
- Produces: `DigitalBrain.Tests.Authoring.StarterBundleSource` — `const string Code`, `const string Pack`, `const string ExperienceId`, nested `Hops.Ask`, `Hops.Result`.

- [ ] **Step 1: Write the failing fast test**

Create `DigitalBrain.Tests/Authoring/StarterBundleTests.cs`:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Tests.Ui;
using Xunit;

namespace DigitalBrain.Tests.Authoring;

public class StarterBundleTests
{
    [Fact]
    public void Starter_ask_hop_renders_input_and_button()
    {
        using var harness = new BundleHarness(
            StarterBundleSource.Code, StarterBundleSource.Pack, StarterBundleSource.ExperienceId);

        var tree = harness.GetTree(StarterBundleSource.Hops.Ask);

        tree.ShouldHaveNodeOfType(DigitalBrain.Core.Ui.TextField);
        tree.ShouldHaveButtonWithLabel("Echo");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~StarterBundleTests"`
Expected: FAIL — compile error, `StarterBundleSource` does not exist.

- [ ] **Step 3: Create the starter bundle source**

Create `DigitalBrain.Tests/Authoring/StarterBundleSource.cs`:

```csharp
namespace DigitalBrain.Tests.Authoring;

// Copy-me starter content bundle. The Code string is the SINGLE source of truth:
// the fast test (BundleHarness) and the render E2E (LiveRenderVerifier) both compile it.
// To make your own bundle: copy this file, rename the type/ids, change the hops.
public static class StarterBundleSource
{
    public const string Pack = "starter";
    public const string ExperienceId = "starter";

    public static class Hops
    {
        public const string Ask = "ask";
        public const string Result = "result";
    }

    public const string Code = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class StarterExperience : KitExperience
{
    protected override UiExperience Define() => Experience("starter", "Starter Bundle")
        .Hop("ask", s => s
            .Text("What should I echo?")
            .TextField("message", "Your message")
            .Button("Echo", "result"))
        .Hop("result", s => s
            .Panel(p => p.Text(state =>
                "You said: " + (state.GetValueOrDefault("message") is { Length: > 0 } m ? m : "nothing"))));
}
""";
}
```

- [ ] **Step 4: Run the fast test to verify it passes**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~StarterBundleTests"`
Expected: PASS (1 passed).

- [ ] **Step 5: Add the skippable render E2E**

Create `DigitalBrain.Tests/E2E/StarterBundleRendersE2ETests.cs`:

```csharp
using DigitalBrain.Tests.Authoring;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class StarterBundleRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task Starter_asks_then_echoes()
    {
        E2EPrerequisites.RequireRenderE2E();

        var driver = new LiveRenderVerifier(
            _fx, pack: StarterBundleSource.Pack, experienceId: StarterBundleSource.ExperienceId);
        await driver.PublishAndInstallAsync(StarterBundleSource.Code, description: "Starter bundle");
        await driver.OpenAsync();

        await driver.SendUserActionAsync("start");
        await driver.AssertSurfaceRenderedAsync(StarterBundleSource.Hops.Ask);

        await driver.SendUserActionAsync(StarterBundleSource.Hops.Result, ("message", "ping"));
        await driver.AssertSurfaceRenderedAsync(StarterBundleSource.Hops.Result);

        await _fx.Page.Locator("text=You said: ping").WaitForAsync(new() { Timeout = 30_000 });
    }
}
```

- [ ] **Step 6: Verify the render test is wired (skips cleanly without prereqs)**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~StarterBundleRendersE2ETests"`
Expected: the test is **skipped** (not failed) with the message "Set RUN_FLUTTER_E2E=true to run the Flutter render E2E." — this confirms it compiles and is gated correctly. (Full render verification is run on demand per the docs in Task 4.)

- [ ] **Step 7: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Tests/Authoring/StarterBundleSource.cs DigitalBrain.Tests/Authoring/StarterBundleTests.cs DigitalBrain.Tests/E2E/StarterBundleRendersE2ETests.cs
git commit -m "$(cat <<'EOF'
test: add copy-me starter bundle with fast + render tests

One source string drives both the in-memory BundleHarness test and the
skippable live-render E2E. The reference path for authoring a new bundle.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Authoring docs + AGENTS.md pointer

**Files:**
- Create: `brain/docs/authoring-a-bundle.md`
- Modify: `brain/AGENTS.md`

**Interfaces:**
- Consumes: nothing (documentation).
- Produces: the blessed-loop reference authors follow.

- [ ] **Step 1: Write the authoring doc**

Create `brain/docs/authoring-a-bundle.md`:

````markdown
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
````

- [ ] **Step 2: Add the pointer to AGENTS.md**

Read `brain/AGENTS.md`, then add this line under the section that describes the test/verification loop (place it where the fast inner-loop / `dotnet test` guidance lives):

```markdown
- **Authoring a bundle:** follow the test-first loop in [`docs/authoring-a-bundle.md`](docs/authoring-a-bundle.md) — define the bundle once as pack source, drive it with `BundleHarness` (fast) and `LiveRenderVerifier` (render).
```

- [ ] **Step 3: Commit**

```bash
cd /e/digitalbraintech/brain
git add docs/authoring-a-bundle.md AGENTS.md
git commit -m "$(cat <<'EOF'
docs: blessed bundle-authoring loop

Document the single-source-of-truth, two-speed authoring loop (BundleHarness
fast path + LiveRenderVerifier render path) and link it from AGENTS.md.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (after all tasks)

- [ ] **Build the whole solution**

Run: `cd /e/digitalbraintech/brain && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Run the full fast authoring-loop test set**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleHarnessTests|FullyQualifiedName~UiTestingFrameworkExamples|FullyQualifiedName~StarterBundleTests|FullyQualifiedName~StarterBundleRendersE2ETests"`
Expected: BundleHarnessTests, UiTestingFrameworkExamples, StarterBundleTests pass; StarterBundleRendersE2ETests skips (no `RUN_FLUTTER_E2E`).

- [ ] **(Optional, on demand) Full render proof**

Build the web bundle (see doc), then:
Run: `cd /e/digitalbraintech/brain && RUN_FLUTTER_E2E=true DIGITALBRAIN_E2E_HEADED=true dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~StarterBundleRendersE2ETests"`
Expected: PASS, with the starter bundle visibly rendering in the browser and a screenshot under `e2e-screenshots/`.

> `aspire doctor` is **not** required for this phase — no AppHost resource-graph changes are made; all changes are test/doc only.
