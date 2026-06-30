# Phase 1a (core) — Bundle Manifest Mechanism Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a bundle declare its product metadata — tier, entry-experience, channels, dependencies — in code (single source of truth), and surface that `BundleManifest` through the real embodiment path so the next slice can materialize it into the marketplace catalog at publish.

**Architecture:** Add a `BundleManifest` record to `DigitalBrain.Core` and a backward-compatible default-null `GetBundleManifest()` on `IPackBehavior`. `KitExperience` overrides it to report `Content` tier, the in-app channel, and its entry-experience id (read from the `UiExperience` it already builds). `EmbodiedPack` gains a one-line passthrough so the compiled-in-ALC behavior's manifest is reachable, and `BundleHarness` exposes it for fast tests. This slice does **not** touch publish, the marketplace cache, or any UI surface — that is the next slice (1a-catalog).

**Tech Stack:** .NET 11 (net11.0), Orleans serialization (`[GenerateSerializer]`/`[property: Id(n)]`), xUnit, the existing `PackAlcEmbodier` + `BundleHarness`.

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`; package versions are central in `Directory.Packages.props`.
- **No vacuous `/// <summary>`** that restates a signature. Self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done** — evidence before assertions.
- `DigitalBrain.Core` has global usings (it uses `IReadOnlyList<>` and Orleans `[GenerateSerializer]`/`[Id]` with no explicit `using` directives — see `Distribution/IPackBehavior.cs`). New Core files follow the same style: `namespace DigitalBrain.Core;` and no `using` lines unless the compiler demands one.
- Backward compatibility: `GetBundleManifest()` must default to `null` so every existing pack keeps compiling and behaving unchanged.
- Look up unfamiliar library/framework APIs via **Context7** before writing code against them.
- Work in the `brain` repo on branch `spec/phase1a-bundle-manifest` (created off `master` at the start). Relative paths; never leak user-profile paths.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

- **Create** `DigitalBrain.Core/Distribution/BundleManifest.cs` — the `BundleTier`/`BundleChannel` enums, `ExperienceRef`, `BundleDependency`, and `BundleManifest` records. Product metadata only (tier/entry-experience/channels/deps); `PackManifest` stays as-is for dispatch + config.
- **Modify** `DigitalBrain.Core/Distribution/IPackBehavior.cs` — add the default-null `GetBundleManifest()` to the `IPackBehavior` interface.
- **Modify** `DigitalBrain.Core/UiKit/KitExperience.cs` — override `GetBundleManifest()` to report `Content` tier, `InApp` channel, and the entry-experience id.
- **Create** `DigitalBrain.Tests/Distribution/BundleManifestTests.cs` — pure unit tests for the default and the `KitExperience` override.
- **Modify** `DigitalBrain.Kernel/Foundry/PackAlcEmbodier.cs` — add a `GetBundleManifest()` passthrough on `EmbodiedPack`.
- **Modify** `DigitalBrain.Tests/Ui/BundleHarness.cs` — expose `Manifest` from the embodied pack.
- **Create** `DigitalBrain.Tests/Distribution/BundleManifestEmbodimentTests.cs` — proves the manifest survives the real Roslyn/ALC embodiment of a shipped bundle.

---

## Task 1: `BundleManifest` in Core + declared by `KitExperience`

**Files:**
- Create: `DigitalBrain.Core/Distribution/BundleManifest.cs`
- Modify: `DigitalBrain.Core/Distribution/IPackBehavior.cs` (add `GetBundleManifest()` to the interface)
- Modify: `DigitalBrain.Core/UiKit/KitExperience.cs` (override `GetBundleManifest()`)
- Test: `DigitalBrain.Tests/Distribution/BundleManifestTests.cs`

**Interfaces:**
- Consumes: `IPackBehavior` (existing), `KitExperience` (existing; has `private UiExperience? _definition`, `protected abstract UiExperience Define()`, and `UiExperience.Id` is public).
- Produces: `DigitalBrain.Core.BundleTier { Substrate, Channel, Content }`, `BundleChannel { InApp, Telegram, Web }`, `ExperienceRef(string ExperienceId, string EntryEvent = "start")`, `BundleDependency(string PackName, string MinVersion)`, `BundleManifest(BundleTier Tier, ExperienceRef? EntryExperience, IReadOnlyList<BundleChannel> Channels, IReadOnlyList<BundleDependency>? Dependencies = null)`; and `IPackBehavior.GetBundleManifest() : BundleManifest?` (default `null`), overridden by `KitExperience`.

- [ ] **Step 1: Write the failing tests**

Create `DigitalBrain.Tests/Distribution/BundleManifestTests.cs`:

```csharp
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class BundleManifestTests
{
    [Fact]
    public void Plain_pack_has_no_bundle_manifest_by_default()
    {
        IPackBehavior pack = new BarePack();
        Assert.Null(pack.GetBundleManifest());
    }

    [Fact]
    public void Kit_experience_reports_content_tier_inapp_channel_and_entry_experience()
    {
        IPackBehavior pack = new GreetExperience();

        var manifest = pack.GetBundleManifest();

        Assert.NotNull(manifest);
        Assert.Equal(BundleTier.Content, manifest!.Tier);
        Assert.Equal("greet", manifest.EntryExperience?.ExperienceId);
        Assert.Contains(BundleChannel.InApp, manifest.Channels);
    }

    private sealed class BarePack : IPackBehavior
    {
        public string Respond(string input) => input;
    }

    private sealed class GreetExperience : KitExperience
    {
        protected override UiExperience Define() => Experience("greet", "Greet")
            .Hop("ask", s => s.Text("hi").Button("Go", "done"))
            .Hop("done", s => s.Text("done"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleManifestTests"`
Expected: FAIL — compile error, `BundleManifest`/`BundleTier`/`BundleChannel` and `GetBundleManifest` do not exist.

- [ ] **Step 3: Create the manifest types**

Create `DigitalBrain.Core/Distribution/BundleManifest.cs`:

```csharp
namespace DigitalBrain.Core;

public enum BundleTier { Substrate, Channel, Content }

public enum BundleChannel { InApp, Telegram, Web }

[GenerateSerializer]
public record ExperienceRef(
    [property: Id(0)] string ExperienceId,
    [property: Id(1)] string EntryEvent = "start");

[GenerateSerializer]
public record BundleDependency(
    [property: Id(0)] string PackName,
    [property: Id(1)] string MinVersion);

// Product-level metadata a bundle declares in code (single source of truth). The next slice materializes
// this into the marketplace catalog at publish so discovery can facet by tier/channel without recompiling.
// PackManifest stays separate — it carries dispatch (HandledSynapseTypes) and config requirements.
[GenerateSerializer]
public record BundleManifest(
    [property: Id(0)] BundleTier Tier,
    [property: Id(1)] ExperienceRef? EntryExperience,
    [property: Id(2)] IReadOnlyList<BundleChannel> Channels,
    [property: Id(3)] IReadOnlyList<BundleDependency>? Dependencies = null);
```

- [ ] **Step 4: Add the default-null method to `IPackBehavior`**

In `DigitalBrain.Core/Distribution/IPackBehavior.cs`, inside the `IPackBehavior` interface, add this default method immediately after the `GetManifest()` line (line 27):

```csharp
    BundleManifest? GetBundleManifest() => null;
```

- [ ] **Step 5: Override it in `KitExperience`**

In `DigitalBrain.Core/UiKit/KitExperience.cs`, add this method to the `KitExperience` class (e.g. immediately after `GetManifest()` on line 16). It reuses the cached `_definition` the class already builds:

```csharp
    public BundleManifest? GetBundleManifest()
    {
        var experience = _definition ??= Define();
        return new BundleManifest(
            BundleTier.Content,
            new ExperienceRef(experience.Id),
            new[] { BundleChannel.InApp });
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleManifestTests"`
Expected: PASS (2 passed).

- [ ] **Step 7: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Core/Distribution/BundleManifest.cs DigitalBrain.Core/Distribution/IPackBehavior.cs DigitalBrain.Core/UiKit/KitExperience.cs DigitalBrain.Tests/Distribution/BundleManifestTests.cs
git commit -m "$(cat <<'MSG'
feat(core): BundleManifest declared in code via GetBundleManifest

Add BundleManifest (tier/entry-experience/channels/deps) + a default-null
IPackBehavior.GetBundleManifest(); KitExperience reports Content/InApp and
its entry-experience id. Backward-compatible — plain packs return null.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Task 2: Surface the manifest through embodiment (`EmbodiedPack` + `BundleHarness`)

**Files:**
- Modify: `DigitalBrain.Kernel/Foundry/PackAlcEmbodier.cs` (`EmbodiedPack` passthrough)
- Modify: `DigitalBrain.Tests/Ui/BundleHarness.cs` (expose `Manifest`)
- Test: `DigitalBrain.Tests/Distribution/BundleManifestEmbodimentTests.cs`

**Interfaces:**
- Consumes: `BundleManifest`, `IPackBehavior.GetBundleManifest()` (Task 1); `EmbodiedPack` (existing, wraps the `IPackBehavior behavior`); `BundleHarness(string packCode, string pack, string experienceId)` (existing, holds `EmbodiedPack _pack`); `MarketplaceSeeds.HelloWorldPackCode` (existing).
- Produces: `EmbodiedPack.GetBundleManifest() : BundleManifest?`; `BundleHarness.Manifest : BundleManifest?`.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Distribution/BundleManifestEmbodimentTests.cs`:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Tests.Ui;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class BundleManifestEmbodimentTests
{
    [Fact]
    public void Embodied_hello_world_surfaces_its_bundle_manifest()
    {
        using var harness = new BundleHarness(
            MarketplaceSeeds.HelloWorldPackCode, pack: "hello-world", experienceId: "hello-world");

        var manifest = harness.Manifest;

        Assert.NotNull(manifest);
        Assert.Equal(BundleTier.Content, manifest!.Tier);
        Assert.Equal("hello-world", manifest.EntryExperience?.ExperienceId);
        Assert.Contains(BundleChannel.InApp, manifest.Channels);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleManifestEmbodimentTests"`
Expected: FAIL — compile error, `BundleHarness.Manifest` does not exist.

- [ ] **Step 3: Add the passthrough on `EmbodiedPack`**

In `DigitalBrain.Kernel/Foundry/PackAlcEmbodier.cs`, in the `EmbodiedPack` class, add this line immediately after `GetManifest()` (line 22):

```csharp
    public BundleManifest? GetBundleManifest() => behavior.GetBundleManifest();
```

- [ ] **Step 4: Expose `Manifest` on `BundleHarness`**

In `DigitalBrain.Tests/Ui/BundleHarness.cs`, add this member to the `BundleHarness` class (e.g. immediately after the constructor):

```csharp
    public BundleManifest? Manifest => _pack.GetBundleManifest();
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleManifestEmbodimentTests"`
Expected: PASS (1 passed). This proves the in-code manifest survives the real compile→ALC→instantiate path — the foundation the next slice (publish materialization) builds on.

- [ ] **Step 6: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Kernel/Foundry/PackAlcEmbodier.cs DigitalBrain.Tests/Ui/BundleHarness.cs DigitalBrain.Tests/Distribution/BundleManifestEmbodimentTests.cs
git commit -m "$(cat <<'MSG'
feat(kernel): expose BundleManifest through EmbodiedPack + BundleHarness

EmbodiedPack delegates GetBundleManifest(); BundleHarness exposes Manifest so
the in-code bundle manifest is readable through the real embodiment path.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Final verification (after both tasks)

- [ ] **Build the whole solution**

Run: `cd /e/digitalbraintech/brain && dotnet build`
Expected: Build succeeded, 0 errors. (Confirms the new `IPackBehavior` default method and the Core types did not break any existing pack or caller.)

- [ ] **Run the manifest test set + the Phase 0 harness tests (no regression)**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~BundleManifestTests|FullyQualifiedName~BundleManifestEmbodimentTests|FullyQualifiedName~BundleHarnessTests|FullyQualifiedName~UiTestingFrameworkExamples|FullyQualifiedName~StarterBundleTests"`
Expected: all pass (the Phase 0 harness tests still green confirms the `IPackBehavior`/`KitExperience`/`EmbodiedPack` changes are non-breaking).

> `aspire doctor` is **not** required for this slice — no AppHost resource-graph change; Core/Kernel/test code only.

## Out of scope (next slice — 1a-catalog)

- Embodying the pack at **publish** to read `GetBundleManifest()` and storing tier/channels/entry-experience as catalog metadata.
- Projecting that metadata into the `MarketplaceListFromPacks` surface.
- Faceting the marketplace list by tier/channel (that is slice 1b).
