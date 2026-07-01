# Phase 1b-facet — Server-Driven Marketplace Faceting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let a user filter the marketplace by tier/channel. Because the client is fully server-driven (the `/marketplace` route is `SizedBox.shrink()`; the list is a neuron-emitted `UiSurface` tree), this is a **backend** change: the marketplace surface gains a row of facet-filter action buttons, and tapping one fires a `FilterMarketplace` synapse that makes the marketplace neuron re-emit a filtered list tree. Zero Flutter changes — the client's `UiSurfaceTreeRenderer` already renders `row` + `neuron:actionbutton` and round-trips the synapse.

**Architecture:** A reusable `UiSurfaceLiveData.MarketplaceTreeSurface(...)` builds the marketplace `column` tree = `[ facetRow, list(filtered packs) ]`, where `facetRow` is a `row` of `neuron:actionbutton` nodes (`All` + one per distinct tier + one per distinct channel), each carrying `SynapseType = FilterMarketplace` and props `{tier}`/`{channel}`. It reuses the existing `MarketplaceListFromPacks` projection (which already includes `tier`/`channels` from 1a-catalog) for the per-pack item dicts, then filters. The startup neuron emits this (unfiltered); `MarketplaceNeuron` handles `FilterMarketplace` by re-emitting it filtered (via `FireAsync` + `HomeFeedBus`). Facet buttons derive from packs whose `BundleManifest` was materialized at publish; seeds shown pre-publish simply show `All` (non-broken).

**Tech Stack:** .NET 11 (net11.0), Orleans (`TestCluster`), xUnit. No client/Flutter changes.

## Global Constraints

- Target framework **net11.0**; never pin `Version="*"`.
- **No vacuous `/// <summary>`**; self-explanatory names; small inline comments only where genuinely non-obvious.
- Tests are executable specs. **Run the relevant tests and confirm they pass before claiming a task done.**
- `DigitalBrain.Core` has global usings; match existing style in `UiSurfaces.cs`/`Synapse.cs`.
- **Non-breaking:** `MarketplaceListFromPacks` is unchanged (reused as-is). The startup marketplace emission keeps `Kind = marketplace-list` with a `tree` prop (same render path); it just swaps the tree contents. `FilterMarketplace` is a new synapse — existing flows are unaffected.
- The client already renders `row`, `neuron:actionbutton` (fires `onEvent('press', props)` → `buildActionEnvelope` → gRPC `Send`), and `list` — do NOT add client code; emit only node types the renderer already handles.
- Look up unfamiliar APIs via **Context7** before writing code.
- Work in the `brain` repo on branch `spec/phase1b-facet-ui` (already checked out). Do NOT `git add` under `.superpowers/`.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

- **Modify** `DigitalBrain.Core/Synapse.cs` — add `FilterMarketplace(string? Tier = null, string? Channel = null)` synapse record.
- **Modify** `DigitalBrain.Core/UiSurfaces.cs` — add `UiSurfaceLiveData.MarketplaceTreeSurface(...)`.
- **Create** `DigitalBrain.Tests/Ui/MarketplaceFacetTests.cs` — pure Core tests (facet row present; filtering by tier/channel).
- **Modify** `DigitalBrain.Kernel/SystemNeurons.cs` — startup emits `MarketplaceTreeSurface` (unfiltered); `MarketplaceNeuron` handles `FilterMarketplace`.
- **Create** `DigitalBrain.Tests/Ui/MarketplaceFilterRoundtripTests.cs` — TestCluster: `FilterMarketplace{Tier}` re-emits a filtered surface.

---

## Task 1: `FilterMarketplace` + `MarketplaceTreeSurface` (Core)

**Files:**
- Modify: `DigitalBrain.Core/Synapse.cs`
- Modify: `DigitalBrain.Core/UiSurfaces.cs`
- Test: `DigitalBrain.Tests/Ui/MarketplaceFacetTests.cs`

**Interfaces:**
- Consumes: `NeuroPack` (with `Manifest`, from 1a-catalog); `UiSurfaceLiveData.MarketplaceListFromPacks(published, installed, userId, sessionId)` — its `Props["packs"]` is a `Dictionary<string,object?>[]` with per-item keys incl. `tier` (string?), `channels` (string[]?); `UiWidgetTree(string Type, IReadOnlyDictionary<string,object?> Props, IReadOnlyList<UiWidgetTree>? Children = null)`; `UiSurface`, `UiSurfaceKinds.MarketplaceList`, `UiSurfaceKeys.{Title,Emitter,SynapseType,Props,Label}`, `DigitalBrain.Core.NeuronUiKit.ActionButton`. VERIFY the exact `UiSurfaceKeys`/`NeuronUiKit` constant names in `UiSurfaces.cs` (search for `ActionButton`, `SynapseType`, `Label`) before use.
- Produces: `FilterMarketplace(string? Tier = null, string? Channel = null)` synapse; `UiSurfaceLiveData.MarketplaceTreeSurface(IReadOnlyList<NeuroPack> published, IReadOnlyList<NeuroPack> installed, string? tierFilter, string? channelFilter, string emitter, string title = "Marketplace", string userId = "anonymous", string? sessionId = null) : UiSurface`.

- [ ] **Step 1: Write the failing tests**

Create `DigitalBrain.Tests/Ui/MarketplaceFacetTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class MarketplaceFacetTests
{
    private static NeuroPack Pack(string name, BundleTier tier, params BundleChannel[] channels) =>
        new(name, "1.0.0", Manifest: new BundleManifest(tier, new ExperienceRef(name), channels));

    private static IEnumerable<UiWidgetTree> Descend(UiWidgetTree n)
    {
        yield return n;
        if (n.Children is null) yield break;
        foreach (var c in n.Children)
            foreach (var d in Descend(c)) yield return d;
    }

    [Fact]
    public void Tree_has_facet_buttons_for_all_and_each_distinct_tier_and_channel()
    {
        var packs = new[]
        {
            Pack("a", BundleTier.Content, BundleChannel.InApp),
            Pack("b", BundleTier.Substrate, BundleChannel.Telegram),
        };

        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            packs, Array.Empty<NeuroPack>(), tierFilter: null, channelFilter: null, emitter: "market-main");

        var tree = (UiWidgetTree)surface.Props["tree"]!;
        var buttons = Descend(tree)
            .Where(n => n.Type == DigitalBrain.Core.NeuronUiKit.ActionButton)
            .Select(n => n.Props[UiSurfaceKeys.Label]?.ToString())
            .ToList();

        Assert.Contains("All", buttons);
        Assert.Contains("Content", buttons);
        Assert.Contains("Substrate", buttons);
        Assert.Contains("Telegram", buttons);
    }

    [Fact]
    public void Facet_button_fires_FilterMarketplace_with_its_tier()
    {
        var packs = new[] { Pack("a", BundleTier.Content, BundleChannel.InApp) };

        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            packs, Array.Empty<NeuroPack>(), null, null, "market-main");

        var tree = (UiWidgetTree)surface.Props["tree"]!;
        var contentBtn = Descend(tree).Single(n =>
            n.Type == DigitalBrain.Core.NeuronUiKit.ActionButton
            && n.Props[UiSurfaceKeys.Label]?.ToString() == "Content");

        Assert.Equal(nameof(FilterMarketplace), contentBtn.Props[UiSurfaceKeys.SynapseType]);
        var btnProps = (IReadOnlyDictionary<string, object?>)contentBtn.Props[UiSurfaceKeys.Props]!;
        Assert.Equal("Content", btnProps["tier"]);
    }

    [Fact]
    public void Tier_filter_restricts_the_list_items()
    {
        var packs = new[]
        {
            Pack("a", BundleTier.Content, BundleChannel.InApp),
            Pack("b", BundleTier.Substrate, BundleChannel.InApp),
        };

        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            packs, Array.Empty<NeuroPack>(), tierFilter: "Content", channelFilter: null, emitter: "market-main");

        var items = (Dictionary<string, object?>[])surface.Props["packs"]!;
        Assert.Single(items);
        Assert.Equal("a", items[0]["name"]);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceFacetTests"`
Expected: FAIL — `FilterMarketplace` / `MarketplaceTreeSurface` do not exist.

- [ ] **Step 3: Add the `FilterMarketplace` synapse**

In `DigitalBrain.Core/Synapse.cs`, near `ListPublished`/`PublishToMarketplace`, add:

```csharp
[GenerateSerializer]
public record FilterMarketplace(
    [property: Id(0)] string? Tier = null,
    [property: Id(1)] string? Channel = null
) : Synapse(nameof(FilterMarketplace), DateTimeOffset.UtcNow);
```

- [ ] **Step 4: Add `MarketplaceTreeSurface`**

In `DigitalBrain.Core/UiSurfaces.cs`, in `UiSurfaceLiveData` (next to `MarketplaceListFromPacks`), add. First confirm the real constant names (`NeuronUiKit.ActionButton`, `UiSurfaceKeys.SynapseType`, `UiSurfaceKeys.Props`, `UiSurfaceKeys.Label`, `UiSurfaceKeys.Title`, `UiSurfaceKeys.Emitter`) — the file already uses several of them:

```csharp
    public static UiSurface MarketplaceTreeSurface(
        IReadOnlyList<NeuroPack> published,
        IReadOnlyList<NeuroPack> installed,
        string? tierFilter,
        string? channelFilter,
        string emitter,
        string title = "Marketplace",
        string userId = "anonymous",
        string? sessionId = null)
    {
        // Reuse the existing projection (name/version/description/installed + tier/channels/entryExperienceId).
        var listSurface = MarketplaceListFromPacks(published, installed, userId, sessionId);
        var allItems = (Dictionary<string, object?>[])listSurface.Props["packs"]!;

        bool Matches(Dictionary<string, object?> item)
        {
            if (tierFilter is not null && item.GetValueOrDefault("tier")?.ToString() != tierFilter) return false;
            if (channelFilter is not null)
            {
                var channels = item.GetValueOrDefault("channels") as string[] ?? Array.Empty<string>();
                if (!channels.Contains(channelFilter)) return false;
            }
            return true;
        }

        var filtered = allItems.Where(Matches).ToArray();

        var tiers = allItems
            .Select(i => i.GetValueOrDefault("tier")?.ToString())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();
        var channelNames = allItems
            .SelectMany(i => i.GetValueOrDefault("channels") as string[] ?? Array.Empty<string>())
            .Distinct()
            .ToList();

        UiWidgetTree FacetButton(string label, string? tier, string? channel) =>
            new(NeuronUiKit.ActionButton, new Dictionary<string, object?>
            {
                [UiSurfaceKeys.Label] = label,
                [UiSurfaceKeys.SynapseType] = nameof(FilterMarketplace),
                [UiSurfaceKeys.Props] = new Dictionary<string, object?> { ["tier"] = tier, ["channel"] = channel }
            });

        var facetButtons = new List<UiWidgetTree> { FacetButton("All", null, null) };
        facetButtons.AddRange(tiers.Select(t => FacetButton(t!, t, null)));
        facetButtons.AddRange(channelNames.Select(c => FacetButton(c, null, c)));

        var tree = new UiWidgetTree("column", new Dictionary<string, object?>(), new List<UiWidgetTree>
        {
            new UiWidgetTree("row", new Dictionary<string, object?>(), facetButtons),
            new UiWidgetTree("list", new Dictionary<string, object?> { ["items"] = filtered })
        });

        return new UiSurface(UiSurfaceKinds.MarketplaceList, new Dictionary<string, object?>
        {
            ["tree"] = tree,
            ["packs"] = filtered,
            [UiSurfaceKeys.Title] = title,
            [UiSurfaceKeys.Emitter] = emitter,
            ["userId"] = userId,
            ["sessionId"] = sessionId,
            ["activeTier"] = tierFilter,
            ["activeChannel"] = channelFilter
        });
    }
```

- [ ] **Step 5: Run to verify pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceFacetTests"`
Expected: PASS (3 passed). If a constant name differs (e.g. `NeuronUiKit.ActionButton`), fix to the real name found in `UiSurfaces.cs` and re-run — do not change the test's asserted labels/behavior.

- [ ] **Step 6: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Core/Synapse.cs DigitalBrain.Core/UiSurfaces.cs DigitalBrain.Tests/Ui/MarketplaceFacetTests.cs
git commit -m "$(cat <<'MSG'
feat(core): MarketplaceTreeSurface with tier/channel facet buttons

Reusable marketplace tree = facet-filter row (All + distinct tiers/channels
as neuron:actionbutton firing FilterMarketplace) + filtered pack list. Adds
the FilterMarketplace synapse. Server-driven; client renders it unchanged.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Task 2: Wire faceting into the neuron + startup (Kernel)

**Files:**
- Modify: `DigitalBrain.Kernel/SystemNeurons.cs` (startup marketplace emission ~lines 137-164; `MarketplaceNeuron` gains a `FilterMarketplace` handler)
- Test: `DigitalBrain.Tests/Ui/MarketplaceFilterRoundtripTests.cs`

**Interfaces:**
- Consumes: `UiSurfaceLiveData.MarketplaceTreeSurface` (Task 1); `FilterMarketplace` (Task 1); existing `MarketplaceNeuron` (`GetPublishedPacks()`/`_publishedCache`, `FireAsync`, `ServiceProvider.GetService<HomeFeedBus>()`, `UiSurfaceRfwBridge.FromUiSurface`, `Self`); the startup neuron's existing `bus`, `publishedForStart`, `Self`.
- Produces: startup emits a faceted marketplace tree; `MarketplaceNeuron` handles `FilterMarketplace` → re-emits a filtered faceted tree.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Ui/MarketplaceFilterRoundtripTests.cs`:

```csharp
using System.Linq;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.TestSupport;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class MarketplaceFilterRoundtripTests
{
    [Fact]
    public async Task Filtering_by_tier_reemits_a_surface_listing_only_matching_bundles()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var market = cluster.GrainFactory.GetGrain<IMarketplaceNeuron>("market-facet-1");
            // hello-world is a KitExperience → Content tier when materialized at publish.
            await market.FireAsync(new PublishToMarketplace(
                "hello-world", "1.0.0", Code: MarketplaceSeeds.HelloWorldPackCode, OwnerId: "tester", CommissionRate: 0.0));
            // a plain behavior pack → no manifest → not Content.
            await market.FireAsync(new PublishToMarketplace(
                "plain", "1.0.0", Code: "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }",
                OwnerId: "tester", CommissionRate: 0.0));

            await market.FireAsync(new FilterMarketplace(Tier: "Content"));

            var surface = (await market.GetTimelineAsync())
                .OfType<UiSurface>()
                .Last(s => s.Kind == UiSurfaceKinds.MarketplaceList);
            var items = (System.Collections.Generic.Dictionary<string, object?>[])surface.Props["packs"]!;

            Assert.Contains(items, i => i["name"]?.ToString() == "hello-world");
            Assert.DoesNotContain(items, i => i["name"]?.ToString() == "plain");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceFilterRoundtripTests"`
Expected: FAIL — `MarketplaceNeuron` does not handle `FilterMarketplace`, so no filtered surface is emitted (or it can't find one).

- [ ] **Step 3: Add the `FilterMarketplace` handler to `MarketplaceNeuron`**

In `DigitalBrain.Kernel/SystemNeurons.cs`, add a handler method to `MarketplaceNeuron` (make the grain implement `IHandle<FilterMarketplace>` if that is how other handlers are wired — check how `HandleAsync(PublishToMarketplace)` is dispatched; the class already has public `HandleAsync(...)` methods, so add one the same way):

```csharp
    public async Task HandleAsync(FilterMarketplace cmd)
    {
        var published = GetPublishedPacks();
        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            published, published, cmd.Tier, cmd.Channel, Self.Value);
        await FireAsync(surface);

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus != null)
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
    }
```

(Match the dispatch mechanism of the existing `HandleAsync(PublishToMarketplace)` — if `MarketplaceNeuron`/`IMarketplaceNeuron` declares `IHandle<PublishToMarketplace>`, add `IHandle<FilterMarketplace>` to the same declaration so `FireAsync(new FilterMarketplace(...))` dispatches here.)

- [ ] **Step 4: Point the startup emission at the faceted tree**

In `DigitalBrain.Kernel/SystemNeurons.cs` startup (the block at ~lines 137-164 that builds `marketTree`/`marketTreeSurface`), replace the hand-built `marketTree` + `marketTreeSurface` with the reusable helper so the initial marketplace view carries the facet row:

```csharp
        var marketTreeSurface = UiSurfaceLiveData.MarketplaceTreeSurface(
            publishedForStart, Array.Empty<NeuroPack>(), tierFilter: null, channelFilter: null,
            emitter: Self.Value, title: "Marketplace");
        await FireAsync(marketTreeSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(marketTreeSurface, Self.Value));
        }
```

(Leave the earlier `marketList`/`MarketplaceListFromPacks` emission at ~line 130 as-is. Note: seeds shown at startup have no materialized `BundleManifest`, so only the `All` facet button appears until packs are published — that's expected and non-broken.)

- [ ] **Step 5: Run to verify pass**

Run: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceFilterRoundtripTests"`
Expected: PASS (1 passed).

- [ ] **Step 6: Commit**

```bash
cd /e/digitalbraintech/brain
git add DigitalBrain.Kernel/SystemNeurons.cs DigitalBrain.Tests/Ui/MarketplaceFilterRoundtripTests.cs
git commit -m "$(cat <<'MSG'
feat(kernel): MarketplaceNeuron handles FilterMarketplace; startup faceted tree

MarketplaceNeuron re-emits a tier/channel-filtered marketplace tree on
FilterMarketplace; startup emits the faceted tree so facet buttons render
from the first view. Server-driven; no client change.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Final verification (after both tasks)

- [ ] **Build**: `cd /e/digitalbraintech/brain && dotnet build` → 0 errors.
- [ ] **Facet + no-regression**: `cd /e/digitalbraintech/brain && dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MarketplaceFacetTests|FullyQualifiedName~MarketplaceFilterRoundtripTests|FullyQualifiedName~MarketplaceListProjectionTests|FullyQualifiedName~CatalogMaterializationTests|FullyQualifiedName~HandlerGrowthTests"` → all pass.

> `aspire doctor` not required — Core/Kernel/test only; NO Flutter/app change (verified: client renders `row`+`neuron:actionbutton`+`list` already).

## Out of scope
- Visual polish of the facet row (active-state highlight, chip styling) — the buttons work; styling is a client follow-up if desired.
- Materializing seed manifests at startup so facets show pre-publish (tied to the existing "manifest durability" follow-up).
- Removing the static demo `forui:FAutocomplete` search box is implicit (the faceted tree replaces the hand-built tree); if the search box is still wanted, add it back as a first child of the column.
