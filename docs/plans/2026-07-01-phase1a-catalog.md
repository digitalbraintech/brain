# Phase 1a-catalog — Materialize Bundle Manifest into the Marketplace Catalog

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** At publish, read a bundle's in-code `BundleManifest` (via the embodier) and store it on the catalog entry, then project tier / channels / entry-experience into the marketplace list surface — so discovery (slice 1b) can facet without recompiling every pack.

**Architecture:** `NeuroPack` gains a nullable `BundleManifest? Manifest` field (the materialized cache). `MarketplaceNeuron.HandleAsync(PublishToMarketplace)` embodies the pack's `Code` once (best-effort), reads `GetBundleManifest()`, disposes the collectible pack immediately, and stores the manifest on the cached `NeuroPack`. `UiSurfaceLiveData.MarketplaceListFromPacks` projects the manifest's tier/channels/entry-experience into each list item (null when a pack has no manifest — non-Kit packs). Materialization is best-effort metadata, never a publish gate (publish-on-green hard-gating is deferred to slice 1b with the trust gate).

**Tech Stack:** .NET 11 (net11.0), Orleans (`[GenerateSerializer]`/`[Id]`, `TestCluster`), xUnit, the existing `IPackEmbodiment`/`EmbodiedPack` + `BundleManifest` from slice 1a-core (merged to `master`).

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`; central package versions in `Directory.Packages.props`.
- **No vacuous `/// <summary>`** that restates a signature. Self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done.**
- `DigitalBrain.Core` has global usings (records use `[GenerateSerializer]`/`[Id]` with no explicit `using` — see `Synapse.cs`, `Distribution/IPackBehavior.cs`). New/edited Core code follows that style.
- Backward compatibility: the new `NeuroPack.Manifest` field is nullable with a `null` default; existing `NeuroPack` construction (positional, via `ToNeuroPack`) and all callers keep compiling unchanged.
- Materialization must be **best-effort**: an embody failure logs a warning and leaves `Manifest` null — it must NOT throw out of the publish handler or block the publish.
- Look up unfamiliar library/framework APIs via **Context7** before writing code against them.
- Work in the `brain` repo on branch `spec/phase1a-catalog` (already checked out). Relative paths; never leak user-profile paths. Do NOT `git add` anything under `.superpowers/`.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

- **Modify** `DigitalBrain.Core/Synapse.cs` — add `[property: Id(10)] BundleManifest? Manifest = null` to the `NeuroPack` record.
- **Modify** `DigitalBrain.Core/UiSurfaces.cs` — project `tier`/`channels`/`entryExperienceId` into each item in `MarketplaceListFromPacks`.
- **Create** `DigitalBrain.Tests/Distribution/MarketplaceListProjectionTests.cs` — pure Core test for the projection.
- **Modify** `DigitalBrain.Kernel/SystemNeurons.cs` — materialize the manifest at publish in `MarketplaceNeuron.HandleAsync(PublishToMarketplace)`.
- **Create** `DigitalBrain.Tests/Distribution/CatalogMaterializationTests.cs` — TestCluster integration test: publish a Kit bundle → the catalog entry carries the manifest.

---

## Task 1: `NeuroPack.Manifest` + marketplace-list projection (Core)

**Files:**
- Modify: `DigitalBrain.Core/Synapse.cs` (`NeuroPack` record, currently lines 225-239)
- Modify: `DigitalBrain.Core/UiSurfaces.cs` (`MarketplaceListFromPacks`, currently lines 773-831)
- Test: `DigitalBrain.Tests/Distribution/MarketplaceListProjectionTests.cs`

**Interfaces:**
- Consumes: `BundleManifest`, `BundleTier`, `BundleChannel`, `ExperienceRef` (slice 1a-core, in `DigitalBrain.Core`); existing `NeuroPack`; existing `UiSurfaceLiveData.MarketplaceListFromPacks(IReadOnlyList<NeuroPack> published, IReadOnlyList<NeuroPack> installed, string userId="anonymous", string? sessionId=null)`.
- Produces: `NeuroPack.Manifest : BundleManifest?` (Id 10, default null); marketplace-list items gain keys `tier` (string?), `channels` (string[]?), `entryExperienceId` (string?).

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Distribution/MarketplaceListProjectionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class MarketplaceListProjectionTests
{
    [Fact]
    public void Marketplace_list_projects_bundle_manifest_facets()
    {
        var manifest = new BundleManifest(
            BundleTier.Content, new ExperienceRef("greet"), new[] { BundleChannel.InApp });
        var pack = new NeuroPack("greet", "1.0.0", Manifest: manifest);

        var surface = UiSurfaceLiveData.MarketplaceListFromPacks(new[] { pack }, Array.Empty<NeuroPack>());

        var items = (Dictionary<string, object?>[])surface.Props["packs"]!;
        var item = items.Single();
        Assert.Equal("Content", item["tier"]);
        Assert.Equal("greet", item["entryExperienceId"]);
        Assert.Contains("InApp", (string[])item["channels"]!);
    }

    [Fact]
    public void Marketplace_list_leaves_facets_null_for_packs_without_a_manifest()
    {
        var pack = new NeuroPack("plain", "1.0.0");

        var surface = UiSurfaceLiveData.MarketplaceListFromPacks(new[] { pack }, Array.Empty<NeuroPack>());

        var item = ((Dictionary<string, object?>[])surface.Props["packs"]!).Single();
        Assert.Null(item["tier"]);
        Assert.Null(item["channels"]);
        Assert.Null(item["entryExperienceId"]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceListProjectionTests"`
Expected: FAIL — compile error (`NeuroPack` has no `Manifest` parameter) and/or missing `tier`/`channels`/`entryExperienceId` keys.

- [ ] **Step 3: Add `Manifest` to `NeuroPack`**

In `DigitalBrain.Core/Synapse.cs`, add a tenth member to the `NeuroPack` record (after `Price` at `[Id(9)]`). The record currently ends:

```csharp
    [property: Id(9)] decimal Price = 0m
);
```

Change it to:

```csharp
    [property: Id(9)] decimal Price = 0m,
    [property: Id(10)] BundleManifest? Manifest = null
);
```

- [ ] **Step 4: Project the facets in `MarketplaceListFromPacks`**

In `DigitalBrain.Core/UiSurfaces.cs`, in the `packs` projection (currently lines 785-794), add three keys to the per-pack dictionary alongside the existing ones:

```csharp
                ["description"] = pack.Description,
                ["installed"] = installedKeys.Contains(PackKey(pack)) || IsPreinstalledLocalPack(pack),
                ["tier"] = pack.Manifest?.Tier.ToString(),
                ["channels"] = pack.Manifest?.Channels.Select(c => c.ToString()).ToArray(),
                ["entryExperienceId"] = pack.Manifest?.EntryExperience?.ExperienceId
```

(The file already uses `System.Linq`; keep the trailing items as shown so the dictionary initializer stays valid.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceListProjectionTests"`
Expected: PASS (2 passed).

- [ ] **Step 6: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Core/Synapse.cs DigitalBrain.Core/UiSurfaces.cs DigitalBrain.Tests/Distribution/MarketplaceListProjectionTests.cs
git commit -m "$(cat <<'MSG'
feat(core): NeuroPack.Manifest + marketplace-list facet projection

Add nullable BundleManifest to NeuroPack (the materialized catalog cache)
and project tier/channels/entryExperienceId into the marketplace list
surface (null for packs without a manifest).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Task 2: Materialize the manifest at publish (Kernel)

**Files:**
- Modify: `DigitalBrain.Kernel/SystemNeurons.cs` (`MarketplaceNeuron.HandleAsync(PublishToMarketplace)` + a private helper)
- Test: `DigitalBrain.Tests/Distribution/CatalogMaterializationTests.cs`

**Interfaces:**
- Consumes: `IPackEmbodiment` (`DigitalBrain.Kernel.Foundry`; resolved via `ServiceProvider.GetService<IPackEmbodiment>()`, registered in the silo — `NeuronTestSiloConfigurator` registers `AddSingleton<IPackEmbodiment, PackAlcEmbodier>()`); `EmbodiedPack.GetBundleManifest()` (slice 1a-core); `NeuroPack.Manifest` (Task 1); existing `ToNeuroPack`, `_publishedCache`, `KeyFor`, `Logger`.
- Produces: catalog entries whose `NeuroPack.Manifest` is populated for bundles that declare one.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Distribution/CatalogMaterializationTests.cs`:

```csharp
using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class CatalogMaterializationTests
{
    [Fact]
    public async Task Publishing_a_kit_bundle_materializes_its_manifest_into_the_catalog()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-catalog-1");
            await market.FireAsync(new PublishToMarketplace(
                "hello-world", "1.0.0", Code: MarketplaceSeeds.HelloWorldPackCode, OwnerId: "tester", CommissionRate: 0.0));
            await market.FireAsync(new ListPublished());

            var listed = (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
            var hello = listed.Single(p => p.Name == "hello-world");

            Assert.NotNull(hello.Manifest);
            Assert.Equal(BundleTier.Content, hello.Manifest!.Tier);
            Assert.Equal("hello-world", hello.Manifest.EntryExperience?.ExperienceId);
            Assert.Contains(BundleChannel.InApp, hello.Manifest.Channels);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
        }
    }
}
```

Note: if `ListPublished` has a non-parameterless constructor, construct it as its definition requires (check `DigitalBrain.Core/Synapse.cs`); the intent is "ask the marketplace to emit its `PublishedList`."

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~CatalogMaterializationTests"`
Expected: FAIL — `hello.Manifest` is null (publish does not yet materialize the manifest).

- [ ] **Step 3: Add the materialization helper**

In `DigitalBrain.Kernel/SystemNeurons.cs`, inside `MarketplaceNeuron`, add this private method (e.g. next to `ToNeuroPack`). If `DigitalBrain.Kernel.Foundry` is not already imported at the top of the file, add `using DigitalBrain.Kernel.Foundry;`:

```csharp
    // Best-effort: embody the pack once to read its in-code BundleManifest so the catalog can facet without
    // recompiling at list time. A compile/embody failure is logged and the pack lists without bundle metadata —
    // materialization is never a publish gate (publish-on-green hard-gating pairs with the trust gate in 1b).
    private NeuroPack MaterializeManifest(NeuroPack pack)
    {
        if (string.IsNullOrEmpty(pack.Code)) return pack;

        var embodiment = ServiceProvider.GetService<IPackEmbodiment>();
        if (embodiment is null) return pack;

        try
        {
            using var embodied = embodiment.Embody(pack.Name, pack.Code);
            var manifest = embodied.GetBundleManifest();
            return manifest is null ? pack : pack with { Manifest = manifest };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Manifest materialization failed for pack {Name}@{Ver}; listing without bundle metadata",
                pack.Name, pack.Version);
            return pack;
        }
    }
```

- [ ] **Step 4: Use it in the publish handler**

In `MarketplaceNeuron.HandleAsync(PublishToMarketplace)`, change the cache-update line (currently line 522):

```csharp
        _publishedCache![KeyFor(cmd.PackName, cmd.Version)] = ToNeuroPack(cmd);
```

to:

```csharp
        _publishedCache![KeyFor(cmd.PackName, cmd.Version)] = MaterializeManifest(ToNeuroPack(cmd));
```

(Leave `EnsureCache()`'s journal-recovery path using plain `ToNeuroPack` — we do not embody every pack on activation; manifests materialize on publish, which is how seeds and live publishes both flow.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~CatalogMaterializationTests"`
Expected: PASS (1 passed). This proves publish embodies the pack and stores its manifest on the catalog entry.

- [ ] **Step 6: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Kernel/SystemNeurons.cs DigitalBrain.Tests/Distribution/CatalogMaterializationTests.cs
git commit -m "$(cat <<'MSG'
feat(kernel): materialize BundleManifest into the catalog at publish

MarketplaceNeuron embodies the pack once at publish (best-effort), reads
its GetBundleManifest(), and stores it on the cached NeuroPack so the
marketplace can facet by tier/channel without recompiling. Embody failures
are logged and never block publish.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Final verification (after both tasks)

- [ ] **Build**

Run: `cd /e/digitalbraintech/brain && dotnet build`
Expected: 0 errors. (Confirms the new `NeuroPack` field did not break any positional-construction caller.)

- [ ] **Run the catalog test set + no-regression on the manifest + marketplace tests**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceListProjectionTests|FullyQualifiedName~CatalogMaterializationTests|FullyQualifiedName~BundleManifestTests|FullyQualifiedName~BundleManifestEmbodimentTests|FullyQualifiedName~HandlerGrowthTests"`
Expected: all pass (HandlerGrowthTests green confirms the publish-path change is non-breaking).

> `aspire doctor` is **not** required — no AppHost resource-graph change; Core/Kernel/test code only.

## Out of scope (next slices)

- Trust-gated publishing (allowlist of trusted publisher keys) — slice **1b**.
- Faceting the marketplace UI by tier/channel (the data lands here; the Flutter facet UI is **1b**).
- Deep-links (**1c**) and Telegram deployed channel (**1d**).
