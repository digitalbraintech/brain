# UI-Kit Fast-Author Slice 0 — "Hello World on rails" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a brand-new UI app authorable as ONE ~15-line C# file that renders full-screen from the marketplace with no kernel recompile/restart — proven end-to-end by a Hello World experience (enter name → press Greet → "Hello {name}!").

**Architecture:** Components are typed `ui:*` nodes carried in a `UiWidgetTree` over `WatchHomeFeed`; the app renders each node via a one-file-per-component ForUI cover. A `KitExperience` Core base + fluent builder turns an author's `Define()` into typed hop surfaces (`UiSurface.ForExperienceHopTree`). The experience-host renders the typed tree and sends button taps back as `ExperienceStep` over the unary `Send` RPC. A `dbt author --watch` CLI command publishes+installs a pack `.cs` into the running cluster live.

**Tech Stack:** .NET (net11.0, Orleans 10.2, Aspire 13.4.6), Roslyn+collectible ALC pack embodiment, gRPC (Grpc.Net.Client), Flutter (ForUI, rfw, go_router), Spectre.Console CLI, xUnit + Reqnroll + Playwright (brain), flutter_test (app).

## Global Constraints

- **net11.0**; AppHost is `NeuroOSPrototype.AppHost`; "Silo" is "Kernel". Central versions in `Directory.Packages.props` — no `Version="*"`.
- **Look up EVERY library API via Context7 before writing code** (Orleans, ForUI, rfw, Grpc.Net.Client, Grpc.Tools, Spectre.Console, flutter_test, go_router). Training data lags releases. This is mandatory per repo rules.
- **Packs compile standalone via Roslyn/ALC against `DigitalBrain.Core` + BCL only, with EXPLICIT `using`s.** Implicit usings compile in the test asm but FAIL the standalone pack compile → silent `PackEmbodimentException` → LLM fallback. Every pack `.cs` string MUST declare its usings.
- **Self-explanatory names; NO vacuous `/// <summary>`** that restates the signature. Small inline comments only where genuinely non-obvious.
- **Node prefix is `ui:`** (e.g. `ui:Screen`, `ui:Text`). Core catalog class is `Ui`. App covers live in `app/lib/ui_kit/`.
- **pack id == experienceId == `hello-world`** (Slice 0 simplification): the published `NeuroPack.Name`, the `ExperienceStep.Pack`, the `generated-<pack>` grain key, and the route `/#/experience/hello-world/hello-world` all use `hello-world`.
- **Verification ritual after changes:** `dotnet build`; `dotnet test --filter "Category!=E2E"`; `flutter analyze` + `flutter test`; `aspire doctor`. One intentional `aspire run` to watch the hot-loop drive Hello World before declaring done.
- Run from `E:\digitalbraintech\brain` (a repo root) and `E:\digitalbraintech\app` (a separate repo root). Use relative paths inside each repo.

---

## File Structure

**brain (`DigitalBrain.Core`):**
- `UiSurfaces.cs` (modify) — add `Ui` node consts + `UiSurface.ForExperienceHopTree`.
- `UiKit/UiExperience.cs` (create) — `UiExperience` + `UiHop` fluent builders.
- `UiKit/KitExperience.cs` (create) — `KitExperience : IPackBehavior` base (state machine + hop emission).
- `MarketplaceSeeds.cs` (modify) — seed the `hello-world` pack.
- `UiSurfaces.cs` → `UiSurfaceLiveData.ExperiencesForPack` (modify) — `hello-world` Run action that navigates to the route.

**brain (`DigitalBrain.Kernel`):**
- `Ui/UiSurfaceRfwBridge.cs` (modify) — widget-tree branch carries experience markers + surfaceId correlation.

**brain (`DigitalBrain.Cli`):**
- `DigitalBrain.Cli.csproj` (modify) — add gRPC client (proto + Grpc.Net.Client/Grpc.Tools/Google.Protobuf).
- `Commands/AuthorCommand.cs` (create) — `dbt author <file> [--watch] [--gateway URL]`.
- `Program.cs` (modify) — dispatch `author` arg before the interactive menu.

**brain (`DigitalBrain.Tests`):**
- `Domains/KitExperienceTests.cs` (create) — Core state machine + node output.
- `Ui/WidgetTreeHopBridgeTests.cs` (create) — bridge marker round-trip.
- `E2E/Packs/HelloWorldPackSource.cs` (create) — the pack `.cs` source string.
- `E2E/HelloWorldRendersE2ETests.cs` (create) — browser acceptance.

**app (`lib/ui_kit/`, create):**
- `ui_form_scope.dart` — `UiKitFormController` + `UiKitFormScope` (InheritedWidget).
- `ui_screen.dart`, `ui_text.dart`, `ui_text_field.dart`, `ui_button.dart`, `ui_panel.dart` — ForUI covers.
- `ui_registry.dart` — `buildUiNode(...)` maps `ui:*` → cover widget.

**app (modify):**
- `lib/rfw_host/rfw_runtime_host.dart` — additive `ui:*` branch in `UiSurfaceTreeRenderer.build`.
- `lib/features/experience/experience_hop_view.dart` — typed-tree render branch.
- `lib/features/experience/experience_host_screen.dart` — wire `_onSurfaceEvent` to Send; auto-fire `start` on open.

**app (tests):**
- `test/ui_kit/ui_kit_widgets_test.dart`, `test/ui_kit/ui_registry_test.dart`, `test/features/experience/experience_hop_view_tree_test.dart`.

---

## Phase 1 — Core: kit vocabulary, authoring API, typed hop emission

### Task 1: `Ui` node constants + `UiSurface.ForExperienceHopTree`

**Files:**
- Modify: `DigitalBrain.Core/UiSurfaces.cs` (add `Ui` class near `NeuronUiKit` ~line 122; add factory in the `UiSurface` record ~after line 86)
- Test: `DigitalBrain.Tests/Domains/KitExperienceTests.cs` (new; first test here)

**Interfaces:**
- Produces: `public static class Ui { Screen, Text, TextField, Button, Panel }` (string consts `"ui:Screen"` …); `UiSurface.ForExperienceHopTree(string pack, string experienceId, string surfaceId, UiWidgetTree tree, string? title = null, string? emitter = null) -> UiSurface` (Kind == `UiSurface.WidgetTreeKind`, Props carry `tree`, `activeExperience`, `experienceId`, `surfaceId`).

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Domains/KitExperienceTests.cs`:

```csharp
using System.Collections.Generic;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Domains;

public class KitExperienceTests
{
    [Fact]
    public void ForExperienceHopTree_carries_tree_and_markers()
    {
        var tree = new UiWidgetTree(Ui.Screen, new Dictionary<string, object?>());
        var surface = UiSurface.ForExperienceHopTree("hello-world", "hello-world", "ask", tree, title: "Hello World");

        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Same(tree, surface.Props["tree"]);
        Assert.Equal("hello-world/hello-world", surface.Props["activeExperience"]);
        Assert.Equal("hello-world", surface.Props["experienceId"]);
        Assert.Equal("ask", surface.Props[UiSurfaceKeys.SurfaceId]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.ForExperienceHopTree" --no-restore`
Expected: FAIL — `Ui` and `ForExperienceHopTree` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

In `DigitalBrain.Core/UiSurfaces.cs`, add the `Ui` catalog class next to `NeuronUiKit`:

```csharp
// Curated UI-kit vocabulary (Slice 0). Each node is a thin ForUI cover on the client.
public static class Ui
{
    public const string Screen = "ui:Screen";
    public const string Text = "ui:Text";
    public const string TextField = "ui:TextField";
    public const string Button = "ui:Button";
    public const string Panel = "ui:Panel";
}
```

In the `UiSurface` record body (after `ForWidgetTree`, before the closing `}` ~line 86), add:

```csharp
    // Typed-tree sibling of ForExperienceHop: an experience hop whose payload is a UiWidgetTree of ui:* nodes.
    // Markers live in Props; UiSurfaceRfwBridge merges them into the wire dataJson and keys correlation on surfaceId.
    public static UiSurface ForExperienceHopTree(
        string pack,
        string experienceId,
        string surfaceId,
        UiWidgetTree tree,
        string? title = null,
        string? emitter = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            ["activeExperience"] = $"{pack}/{experienceId}",
            ["experienceId"] = experienceId,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;
        return new UiSurface(WidgetTreeKind, props);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.ForExperienceHopTree" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Core/UiSurfaces.cs DigitalBrain.Tests/Domains/KitExperienceTests.cs
git commit -m "feat(core): ui: node consts + UiSurface.ForExperienceHopTree"
```

---

### Task 2: Bridge carries experience markers for widget-tree hops

**Files:**
- Modify: `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs` (the `WidgetTreeKind` branch, ~lines 55-63)
- Test: `DigitalBrain.Tests/Ui/WidgetTreeHopBridgeTests.cs` (new)

**Interfaces:**
- Consumes: `UiSurface.ForExperienceHopTree` (Task 1), `RfwCard` (`DigitalBrain.Core/Ui/RfwCard.cs`).
- Produces: an `RfwCard` whose `RootWidget == "WidgetTreeHost"`, `CorrelationId == surfaceId`, and `DataJson` is a JSON object with keys `tree`, `kind`, `activeExperience`, `experienceId`, `surfaceId`.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Ui/WidgetTreeHopBridgeTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class WidgetTreeHopBridgeTests
{
    [Fact]
    public void WidgetTree_hop_surface_carries_markers_and_keys_correlation_on_surfaceId()
    {
        var tree = new UiWidgetTree(Ui.Screen, new Dictionary<string, object?>(),
            new List<UiWidgetTree> { new(Ui.Text, new Dictionary<string, object?> { ["text"] = "hi" }) });
        var surface = UiSurface.ForExperienceHopTree("hello-world", "hello-world", "ask", tree);

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, "hello-world");

        Assert.Equal("WidgetTreeHost", card.RootWidget);
        Assert.Equal("ask", card.CorrelationId);

        using var doc = JsonDocument.Parse(card.DataJson);
        var root = doc.RootElement;
        Assert.Equal("hello-world/hello-world", root.GetProperty("activeExperience").GetString());
        Assert.Equal("ask", root.GetProperty("surfaceId").GetString());
        Assert.True(root.TryGetProperty("tree", out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~WidgetTreeHopBridgeTests" --no-restore`
Expected: FAIL — current branch serializes only `{ tree, kind }` and sets `CorrelationId = surface.SynapseId`, so `activeExperience`/`surfaceId` are absent and correlation ≠ "ask".

- [ ] **Step 3: Write minimal implementation**

In `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs`, replace the existing `WidgetTreeKind` branch:

```csharp
    if (surface.Kind == UiSurface.WidgetTreeKind && surface.Props.TryGetValue("tree", out var treeObj))
    {
        // For widget trees we still use a lightweight RFW wrapper that the client knows how to expand into ForUI + sub RFW.
        // Real power: neuron can emit ForRfw or ForWidgetTree and client renders the tree.
        var dataJson = JsonSerializer.Serialize(new { tree = treeObj, kind = surface.Kind });
        return new RfwCard("digitalbrain", "WidgetTreeHost", dataJson)
        {
            CorrelationId = surface.CorrelationId ?? surface.SynapseId
        };
    }
```

with:

```csharp
    if (surface.Kind == UiSurface.WidgetTreeKind && surface.Props.TryGetValue("tree", out var treeObj))
    {
        var payload = new Dictionary<string, object?> { ["tree"] = treeObj, ["kind"] = surface.Kind };
        // Carry experience markers so the experience host can match the hop and key its semantics on the surfaceId.
        foreach (var markerKey in new[] { "activeExperience", "experienceId", UiSurfaceKeys.SurfaceId })
        {
            if (surface.Props.TryGetValue(markerKey, out var markerValue) && markerValue is not null)
                payload[markerKey] = markerValue;
        }
        var correlation = surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var sid) && sid is string sidStr && sidStr.Length > 0
            ? sidStr
            : surface.CorrelationId ?? surface.SynapseId;
        return new RfwCard("digitalbrain", "WidgetTreeHost", JsonSerializer.Serialize(payload))
        {
            CorrelationId = correlation
        };
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~WidgetTreeHopBridgeTests" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs DigitalBrain.Tests/Ui/WidgetTreeHopBridgeTests.cs
git commit -m "feat(kernel): widget-tree hop bridge carries experience markers + surfaceId correlation"
```

---

### Task 3: Fluent authoring builder + `KitExperience` base

**Files:**
- Create: `DigitalBrain.Core/UiKit/UiExperience.cs`
- Create: `DigitalBrain.Core/UiKit/KitExperience.cs`
- Test: `DigitalBrain.Tests/Domains/KitExperienceTests.cs` (add tests)

**Interfaces:**
- Consumes: `UiWidgetTree`, `Ui`, `UiSurface.ForExperienceHopTree` (Task 1); `IPackBehavior`, `PackManifest`, `SynapseType`, `Synapse`, `ExperienceStep` (`DigitalBrain.Core`).
- Produces:
  - `UiExperience` with `string Id`, `string Name`, `UiExperience Hop(string hopId, Action<UiHop> body)`.
  - `UiHop` with `UiHop Text(string)`, `UiHop Text(Func<IReadOnlyDictionary<string,string>,string>)`, `UiHop TextField(string name, string placeholder = "")`, `UiHop Button(string label, string goTo)`, `UiHop Panel(Action<UiHop> body)`.
  - `abstract class KitExperience : IPackBehavior` with `protected abstract UiExperience Define();` and `protected static UiExperience Experience(string id, string name)`. Handles `ExperienceStep`: merges `Args` into accumulated state, resolves hop (`"start"` → first hop, else by id), emits one `UiSurface.ForExperienceHopTree`. `ui:Button` nodes get `pack`/`experienceId` injected at emit time.

- [ ] **Step 1: Write the failing tests**

Append to `DigitalBrain.Tests/Domains/KitExperienceTests.cs` (inside the class):

```csharp
    private sealed class GreetPack : KitExperience
    {
        protected override UiExperience Define() => Experience("hello-world", "Hello World")
            .Hop("ask", s => s
                .Text("What's your name?")
                .TextField("name", "Your name")
                .Button("Greet", "greeting"))
            .Hop("greeting", s => s
                .Panel(p => p.Text(state =>
                    $"Hello {(state.TryGetValue("name", out var n) && n.Length > 0 ? n : "World")}!")));
    }

    private static ExperienceStep Step(string eventName, params (string, string)[] args) =>
        new("hello-world", "hello-world", eventName, args.ToDictionary(a => a.Item1, a => a.Item2));

    [Fact]
    public void Start_emits_ask_hop_with_text_field_and_button()
    {
        var pack = new GreetPack();
        var outputs = pack.Handle(Step("start"));

        var surface = Assert.IsType<UiSurface>(Assert.Single(outputs));
        Assert.Equal("ask", surface.Props[UiSurfaceKeys.SurfaceId]);
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(Ui.Screen, tree.Type);
        Assert.Collection(tree.Children!,
            n => Assert.Equal(Ui.Text, n.Type),
            n => Assert.Equal(Ui.TextField, n.Type),
            n =>
            {
                Assert.Equal(Ui.Button, n.Type);
                Assert.Equal("greeting", n.Props["eventName"]);
                Assert.Equal("hello-world", n.Props["pack"]);          // injected at emit time
                Assert.Equal("hello-world", n.Props["experienceId"]);
            });
    }

    [Fact]
    public void Greeting_hop_bakes_captured_name_into_text()
    {
        var pack = new GreetPack();
        pack.Handle(Step("start"));
        var outputs = pack.Handle(Step("greeting", ("name", "Alice")));

        var surface = Assert.IsType<UiSurface>(Assert.Single(outputs));
        Assert.Equal("greeting", surface.Props[UiSurfaceKeys.SurfaceId]);
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        var panel = Assert.Single(tree.Children!);
        Assert.Equal(Ui.Panel, panel.Type);
        var text = Assert.Single(panel.Children!);
        Assert.Equal("Hello Alice!", text.Props["text"]);
    }

    [Fact]
    public void Manifest_handles_experience_step_only()
    {
        var pack = new GreetPack();
        Assert.True(pack.CanHandle(Step("start")));
        Assert.Contains(pack.GetManifest().HandledSynapseTypes, t => t.Value == nameof(ExperienceStep));
    }
```

Add `using System.Linq;` and `using DigitalBrain.Core;` at the top of the file if not present.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests" --no-restore`
Expected: FAIL — `KitExperience`, `UiExperience`, `UiHop` do not exist.

- [ ] **Step 3: Write the implementation**

Create `DigitalBrain.Core/UiKit/UiExperience.cs`:

```csharp
namespace DigitalBrain.Core;

// Author-facing fluent definition of an experience: an ordered set of named hops, the first of which is the entry.
public sealed class UiExperience
{
    public string Id { get; }
    public string Name { get; }
    internal List<UiHop> Hops { get; } = new();

    internal UiExperience(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public UiExperience Hop(string hopId, Action<UiHop> body)
    {
        var hop = new UiHop(hopId);
        body(hop);
        Hops.Add(hop);
        return this;
    }
}

// One hop: an ordered list of node factories. A factory may depend on the accumulated flow state
// (e.g. the greeting text uses the captured name), so the literal is computed at emit time, keeping the client dumb.
public sealed class UiHop
{
    public string Id { get; }
    internal List<Func<IReadOnlyDictionary<string, string>, UiWidgetTree>> Factories { get; } = new();

    internal UiHop(string id) => Id = id;

    public UiHop Text(string text)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Text, new Dictionary<string, object?> { ["text"] = text }));
        return this;
    }

    public UiHop Text(Func<IReadOnlyDictionary<string, string>, string> text)
    {
        Factories.Add(state => new UiWidgetTree(Ui.Text, new Dictionary<string, object?> { ["text"] = text(state) }));
        return this;
    }

    public UiHop TextField(string name, string placeholder = "")
    {
        Factories.Add(_ => new UiWidgetTree(Ui.TextField,
            new Dictionary<string, object?> { ["name"] = name, ["placeholder"] = placeholder }));
        return this;
    }

    public UiHop Button(string label, string goTo)
    {
        Factories.Add(_ => new UiWidgetTree(Ui.Button,
            new Dictionary<string, object?> { ["label"] = label, ["eventName"] = goTo }));
        return this;
    }

    public UiHop Panel(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(Ui.Panel, new Dictionary<string, object?>(),
            inner.Factories.Select(factory => factory(state)).ToList()));
        return this;
    }
}
```

Create `DigitalBrain.Core/UiKit/KitExperience.cs`:

```csharp
namespace DigitalBrain.Core;

// Base for a typed-C# UI app authored against the ui: kit. Subclasses implement Define() with the fluent builder.
// The base owns the ExperienceStep state machine, accumulates flow state, and emits each hop as a widget-tree surface.
public abstract class KitExperience : IPackBehavior
{
    private readonly Dictionary<string, string> _state = new();
    private UiExperience? _definition;

    protected abstract UiExperience Define();

    protected static UiExperience Experience(string id, string name) => new(id, name);

    public string Respond(string input) => $"experience:{input}";

    public PackManifest GetManifest() => new(new[] { new SynapseType(nameof(ExperienceStep)) });

    public bool CanHandle(Synapse synapse) => synapse is ExperienceStep;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not ExperienceStep step) return Array.Empty<Synapse>();

        foreach (var (key, value) in step.Args) _state[key] = value;

        var experience = _definition ??= Define();
        if (experience.Hops.Count == 0) return Array.Empty<Synapse>();

        var hopId = step.EventName == "start" ? experience.Hops[0].Id : step.EventName;
        var hop = experience.Hops.FirstOrDefault(h => h.Id == hopId);
        if (hop is null) return Array.Empty<Synapse>();

        var screen = BuildScreen(hop, _state, experience.Id);
        return new Synapse[]
        {
            UiSurface.ForExperienceHopTree(experience.Id, experience.Id, hop.Id, screen, title: experience.Name, emitter: experience.Id)
        };
    }

    private static UiWidgetTree BuildScreen(UiHop hop, IReadOnlyDictionary<string, string> state, string id)
    {
        var children = hop.Factories.Select(factory => Inject(factory(state), id)).ToList();
        return new UiWidgetTree(Ui.Screen, new Dictionary<string, object?>(), children);
    }

    // The button must carry pack + experienceId so the client can address the ExperienceStep back to this pack.
    // Hops do not know those ids, so the base injects them when it emits.
    private static UiWidgetTree Inject(UiWidgetTree node, string id)
    {
        if (node.Type == Ui.Button)
        {
            var props = new Dictionary<string, object?>(node.Props) { ["pack"] = id, ["experienceId"] = id };
            return node with { Props = props };
        }
        if (node.Children is { } children)
        {
            return node with { Children = children.Select(child => Inject(child, id)).ToList() };
        }
        return node;
    }
}
```

Note: `UiWidgetTree` is a positional record `(Type, Props, Children, RfwSource, RfwRoot)`; `with { Props = … }` / `with { Children = … }` work. Ensure `DigitalBrain.Core` has `<ImplicitUsings>enable</ImplicitUsings>` (it does — these Core files may use implicit usings; only PACK source strings must be explicit). Verify `System`, `System.Collections.Generic`, `System.Linq` resolve.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests" --no-restore`
Expected: PASS (all KitExperienceTests).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Core/UiKit/ DigitalBrain.Tests/Domains/KitExperienceTests.cs
git commit -m "feat(core): KitExperience fluent authoring base + ui: hop emission"
```

---

### Task 4: Seed the `hello-world` pack + marketplace Run-to-route action

**Files:**
- Create: `DigitalBrain.Tests/E2E/Packs/HelloWorldPackSource.cs` (the canonical pack `.cs` source, shared by the seed and the E2E)
- Modify: `DigitalBrain.Core/MarketplaceSeeds.cs` (add the `hello-world` `NeuroPack`; mark preinstalled)
- Modify: `DigitalBrain.Core/UiSurfaces.cs` → `UiSurfaceLiveData.ExperiencesForPack` + `IsPreinstalledLocalPack`
- Test: covered by Task 10 (E2E); add a fast guard test below.

**Interfaces:**
- Consumes: `KitExperience` (Task 3).
- Produces: `HelloWorldPackSource.Code` (a standalone-compilable pack source string); a seeded `NeuroPack("hello-world", "1.0.0", …)`; an experiences Run action whose props include `targetSurfaceKind = "/experience/hello-world/hello-world"`.

- [ ] **Step 1: Write the failing test**

Append to `DigitalBrain.Tests/Domains/KitExperienceTests.cs`:

```csharp
    [Fact]
    public void HelloWorld_pack_source_is_present_and_explicit_usings()
    {
        var code = DigitalBrain.Tests.E2E.Packs.HelloWorldPackSource.Code;
        Assert.Contains("using DigitalBrain.Core;", code);
        Assert.Contains(": KitExperience", code);
        Assert.DoesNotContain("/* TODO", code);
    }

    [Fact]
    public void Seeds_include_hello_world_pack()
    {
        Assert.Contains(MarketplaceSeeds.LocalUiPacks, p => p.Name == "hello-world");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests.HelloWorld_pack_source|FullyQualifiedName~KitExperienceTests.Seeds_include" --no-restore`
Expected: FAIL — `HelloWorldPackSource` missing; seed absent.

- [ ] **Step 3: Implement**

Create `DigitalBrain.Tests/E2E/Packs/HelloWorldPackSource.cs`:

```csharp
namespace DigitalBrain.Tests.E2E.Packs;

// The canonical Slice 0 demo: a whole UI app in one ~15-line file. Shared by the marketplace seed and the E2E.
// MUST use explicit usings — packs compile standalone via Roslyn/ALC against DigitalBrain.Core only.
public static class HelloWorldPackSource
{
    public const string Code = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class HelloWorldExperience : KitExperience
{
    protected override UiExperience Define() => Experience("hello-world", "Hello World")
        .Hop("ask", s => s
            .Text("What's your name?")
            .TextField("name", "Your name")
            .Button("Greet", "greeting"))
        .Hop("greeting", s => s
            .Panel(p => p.Text(state =>
                $"Hello {(state.TryGetValue(\"name\", out var n) && n.Length > 0 ? n : \"World\")}!")));
}
""";
}
```

In `DigitalBrain.Core/MarketplaceSeeds.cs`, the seed needs the same source. Add a `NeuroPack` to `LocalUiPacks` (place it before `Dummy.BehaviorPack`). Because Core cannot reference the test project, inline the identical source string here:

```csharp
        new NeuroPack(
            "hello-world",
            "1.0.0",
            "digitalbraintech",
            false,
            0.0,
            """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class HelloWorldExperience : KitExperience
{
    protected override UiExperience Define() => Experience("hello-world", "Hello World")
        .Hop("ask", s => s
            .Text("What's your name?")
            .TextField("name", "Your name")
            .Button("Greet", "greeting"))
        .Hop("greeting", s => s
            .Panel(p => p.Text(state =>
                $"Hello {(state.TryGetValue(\"name\", out var n) && n.Length > 0 ? n : \"World\")}!")));
}
""",
            "Hello World — the smallest ui: kit app: enter your name, press Greet, see a greeting."),
```

In `DigitalBrain.Core/UiSurfaces.cs`, in `UiSurfaceLiveData.IsPreinstalledLocalPack`, add the `hello-world` name so it shows installed in the marketplace list:

```csharp
    private static bool IsPreinstalledLocalPack(NeuroPack pack) =>
        pack.Name.StartsWith("DigitalBrain.UI", StringComparison.Ordinal) ||
        pack.Name.StartsWith("DigitalBrain.Experience", StringComparison.Ordinal) ||
        pack.Name.Equals("hello-world", StringComparison.OrdinalIgnoreCase) ||
        pack.Name.Contains("Dummy", StringComparison.OrdinalIgnoreCase);
```

In `UiSurfaceLiveData.ExperiencesForPack`, add a branch BEFORE the final `else` (the generic authored-pack branch):

```csharp
        else if (pack.Name.Equals("hello-world", StringComparison.OrdinalIgnoreCase))
        {
            yield return ExperienceRow(
                pack,
                "open",
                "Open",
                "experience",
                "Enter your name and get a greeting.",
                UiSurfaceSamples.SynapseAction(
                    "open-hello-world",
                    "Open",
                    nameof(ExperienceUsed),
                    new Dictionary<string, object?>
                    {
                        ["packName"] = pack.Name,
                        ["action"] = "open",
                        // The launcher fbutton forwards props["targetSurfaceKind"] to onNavSelected → shell _goTo → context.go.
                        ["targetSurfaceKind"] = "/experience/hello-world/hello-world"
                    }),
                userId,
                sessionId);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~KitExperienceTests" --no-restore`
Expected: PASS.

- [ ] **Step 5: Build + commit**

```bash
dotnet build Brain.slnx -clp:ErrorsOnly
git add DigitalBrain.Core/MarketplaceSeeds.cs DigitalBrain.Core/UiSurfaces.cs DigitalBrain.Tests/E2E/Packs/HelloWorldPackSource.cs DigitalBrain.Tests/Domains/KitExperienceTests.cs
git commit -m "feat(core): seed hello-world pack + marketplace Run-to-experience-route action"
```

---

## Phase 2 — App: ui_kit covers, tree-render branch, host wiring

> Run all app steps from `E:\digitalbraintech\app`. Verify every ForUI/Flutter widget signature via Context7 first (`FButton`, `FTextField`, `FCard`, `InheritedWidget`, `TextEditingController`).

### Task 5: `ui_kit` cover widgets + registry

**Files:**
- Create: `lib/ui_kit/ui_form_scope.dart`, `lib/ui_kit/ui_screen.dart`, `lib/ui_kit/ui_text.dart`, `lib/ui_kit/ui_text_field.dart`, `lib/ui_kit/ui_button.dart`, `lib/ui_kit/ui_panel.dart`, `lib/ui_kit/ui_registry.dart`
- Test: `test/ui_kit/ui_kit_widgets_test.dart`, `test/ui_kit/ui_registry_test.dart`

**Interfaces:**
- Produces:
  - `class UiKitFormController extends ChangeNotifier { Map<String,String> get values; void set(String name, String value); }`
  - `class UiKitFormScope extends InheritedWidget { final UiKitFormController controller; static UiKitFormController? of(BuildContext c); }`
  - `class UiKitScreen extends StatefulWidget { const UiKitScreen({required List<Widget> children}); }`
  - `class UiKitText extends StatelessWidget { const UiKitText({required String text}); }`
  - `class UiKitTextField extends StatefulWidget { const UiKitTextField({required String name, String placeholder}); }`
  - `class UiKitButton extends StatelessWidget { const UiKitButton({required String label, required String pack, required String experienceId, required String eventName, required RemoteEventHandler onEvent}); }`
  - `class UiKitPanel extends StatelessWidget { const UiKitPanel({required List<Widget> children}); }`
  - `Widget buildUiNode(String type, Map<String,Object?> props, List childrenList, RemoteEventHandler onEvent, {required Widget Function(Map<String,Object?>) buildChild})`
- Consumes: `RemoteEventHandler` (`package:rfw/rfw.dart`), ForUI (`package:forui/forui.dart`).

- [ ] **Step 1: Write the failing tests**

Create `test/ui_kit/ui_kit_widgets_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_button.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_form_scope.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_screen.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text_field.dart';

Widget _host(Widget child) =>
    MaterialApp(home: FTheme(data: FThemes.zinc.light, child: Scaffold(body: child)));

void main() {
  testWidgets('UiKitText renders its text', (tester) async {
    await tester.pumpWidget(_host(const UiKitText(text: 'What\'s your name?')));
    expect(find.text('What\'s your name?'), findsOneWidget);
  });

  testWidgets('TextField writes to the form scope and Button emits captured value', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitScreen(children: [
      const UiKitTextField(name: 'name'),
      UiKitButton(
        label: 'Greet',
        pack: 'hello-world',
        experienceId: 'hello-world',
        eventName: 'greeting',
        onEvent: (n, a) => captured = a,
      ),
    ])));

    await tester.enterText(find.byType(FTextField), 'Alice');
    await tester.tap(find.text('Greet'));
    await tester.pump();

    final props = captured!['props'] as Map<String, Object?>;
    expect(captured!['synapseType'], 'ExperienceStep');
    expect(props['pack'], 'hello-world');
    expect(props['eventName'], 'greeting');
    expect(props['name'], 'Alice');
  });
}
```

Create `test/ui_kit/ui_registry_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text.dart';

void main() {
  test('buildUiNode maps ui:Text to UiKitText', () {
    final w = buildUiNode('ui:text', {'text': 'hi'}, const [], (_, __) {}, buildChild: (_) => const SizedBox());
    expect(w, isA<UiKitText>());
    expect((w as UiKitText).text, 'hi');
  });
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `flutter test test/ui_kit/`
Expected: FAIL — files/classes do not exist.

- [ ] **Step 3: Implement the covers**

Create `lib/ui_kit/ui_form_scope.dart`:

```dart
import 'package:flutter/widgets.dart';

/// Holds the values captured by ui:TextField nodes within a single hop, so ui:Button can read them at press time.
class UiKitFormController extends ChangeNotifier {
  final Map<String, String> _values = {};
  Map<String, String> get values => Map.unmodifiable(_values);
  void set(String name, String value) => _values[name] = value;
}

class UiKitFormScope extends InheritedWidget {
  const UiKitFormScope({super.key, required this.controller, required super.child});

  final UiKitFormController controller;

  static UiKitFormController? of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<UiKitFormScope>()?.controller;

  @override
  bool updateShouldNotify(UiKitFormScope oldWidget) => controller != oldWidget.controller;
}
```

Create `lib/ui_kit/ui_screen.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'ui_form_scope.dart';

/// Vertical hop root. Owns the form controller and exposes it to descendant ui:TextField / ui:Button covers.
class UiKitScreen extends StatefulWidget {
  const UiKitScreen({super.key, required this.children});
  final List<Widget> children;

  @override
  State<UiKitScreen> createState() => _UiKitScreenState();
}

class _UiKitScreenState extends State<UiKitScreen> {
  final UiKitFormController _controller = UiKitFormController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return UiKitFormScope(
      controller: _controller,
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (final child in widget.children)
              Padding(padding: const EdgeInsets.symmetric(vertical: 8), child: child),
          ],
        ),
      ),
    );
  }
}
```

Create `lib/ui_kit/ui_text.dart`:

```dart
import 'package:flutter/widgets.dart';

class UiKitText extends StatelessWidget {
  const UiKitText({super.key, required this.text});
  final String text;

  @override
  Widget build(BuildContext context) =>
      Text(text, textAlign: TextAlign.center);
}
```

Create `lib/ui_kit/ui_text_field.dart` (verify `FTextField` API via Context7):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'ui_form_scope.dart';

class UiKitTextField extends StatefulWidget {
  const UiKitTextField({super.key, required this.name, this.placeholder = ''});
  final String name;
  final String placeholder;

  @override
  State<UiKitTextField> createState() => _UiKitTextFieldState();
}

class _UiKitTextFieldState extends State<UiKitTextField> {
  final TextEditingController _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return FTextField(
      controller: _controller,
      hint: widget.placeholder,
      onChange: (value) => UiKitFormScope.of(context)?.set(widget.name, value),
    );
  }
}
```

Create `lib/ui_kit/ui_button.dart` (verify `FButton` API via Context7):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_form_scope.dart';

class UiKitButton extends StatelessWidget {
  const UiKitButton({
    super.key,
    required this.label,
    required this.pack,
    required this.experienceId,
    required this.eventName,
    required this.onEvent,
  });

  final String label;
  final String pack;
  final String experienceId;
  final String eventName;
  final RemoteEventHandler onEvent;

  @override
  Widget build(BuildContext context) {
    return FButton(
      onPress: () {
        final values = UiKitFormScope.of(context)?.values ?? const {};
        onEvent('press', {
          'synapseType': 'ExperienceStep',
          'props': {
            'pack': pack,
            'experienceId': experienceId,
            'eventName': eventName,
            ...values,
          },
        });
      },
      child: Text(label),
    );
  }
}
```

Create `lib/ui_kit/ui_panel.dart` (verify `FCard` API via Context7):

```dart
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

class UiKitPanel extends StatelessWidget {
  const UiKitPanel({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return FCard(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    );
  }
}
```

Create `lib/ui_kit/ui_registry.dart`:

```dart
import 'package:flutter/widgets.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;
import 'ui_button.dart';
import 'ui_panel.dart';
import 'ui_screen.dart';
import 'ui_text.dart';
import 'ui_text_field.dart';

/// Maps a ui:* node (type already lower-cased by the tree renderer) to its ForUI cover widget.
/// [buildChild] recurses back into the tree renderer for container children (ui:Screen, ui:Panel).
Widget buildUiNode(
  String type,
  Map<String, Object?> props,
  List childrenList,
  RemoteEventHandler onEvent, {
  required Widget Function(Map<String, Object?>) buildChild,
}) {
  List<Widget> kids() =>
      childrenList.cast<Map<String, Object?>>().map(buildChild).toList();
  String s(String key) => (props[key] ?? '').toString();

  switch (type) {
    case 'ui:screen':
      return UiKitScreen(children: kids());
    case 'ui:panel':
      return UiKitPanel(children: kids());
    case 'ui:text':
      return UiKitText(text: s('text'));
    case 'ui:textfield':
      return UiKitTextField(name: s('name'), placeholder: s('placeholder'));
    case 'ui:button':
      return UiKitButton(
        label: s('label'),
        pack: s('pack'),
        experienceId: s('experienceId'),
        eventName: s('eventName'),
        onEvent: onEvent,
      );
    default:
      return const SizedBox.shrink();
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `flutter test test/ui_kit/`
Expected: PASS. (If a ForUI signature differs — e.g. `onChange` vs `onChanged`, or `FButton` requires a `style`/`variant` — fix per Context7 and re-run.)

- [ ] **Step 5: Commit**

```bash
git add lib/ui_kit/ test/ui_kit/
git commit -m "feat(ui_kit): 5 ForUI-cover components + registry + form-scope capture"
```

---

### Task 6: `ui:*` dispatch branch in the tree renderer

**Files:**
- Modify: `lib/rfw_host/rfw_runtime_host.dart` (`UiSurfaceTreeRenderer.build`, after the type/props/children extraction ~line 90)
- Test: `test/ui_kit/ui_registry_test.dart` (extend) — render a ui:Screen tree through the renderer

**Interfaces:**
- Consumes: `buildUiNode` (Task 5); `UiSurfaceTreeRenderer.build(Map node, RemoteEventHandler onEvent, {required RfwRuntimeHost rfwHost, void Function(String)? onNavSelected, String? activeTarget})`.
- Produces: `build` returns the ui:* cover widget when `type.startsWith('ui:')`.

- [ ] **Step 1: Write the failing test**

Append to `test/ui_kit/ui_registry_test.dart`:

```dart
// add imports at top:
// import 'package:flutter/material.dart';
// import 'package:flutter_test/flutter_test.dart';
// import 'package:forui/forui.dart';
// import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

  testWidgets('tree renderer renders a ui:Screen tree', (tester) async {
    final tree = {
      'Type': 'ui:Screen',
      'Props': <String, Object?>{},
      'Children': [
        {'Type': 'ui:Text', 'Props': {'text': 'hi there'}, 'Children': []},
      ],
    };
    await tester.pumpWidget(MaterialApp(
      home: FTheme(
        data: FThemes.zinc.light,
        child: Scaffold(
          body: UiSurfaceTreeRenderer().build(tree, (_, __) {}, rfwHost: RfwRuntimeHost()),
        ),
      ),
    ));
    expect(find.text('hi there'), findsOneWidget);
  });
```

(Convert the existing `ui_registry_test.dart` `main` to include both `test(...)` and `testWidgets(...)`; keep the prior unit test.)

- [ ] **Step 2: Run test to verify it fails**

Run: `flutter test test/ui_kit/ui_registry_test.dart`
Expected: FAIL — renderer has no `ui:` branch, returns a Container/fallback; `find.text('hi there')` finds nothing.

- [ ] **Step 3: Implement**

In `lib/rfw_host/rfw_runtime_host.dart`, inside `UiSurfaceTreeRenderer.build`, immediately AFTER the `childrenList` extraction (~line 90) and BEFORE the first node-type `if`, add:

```dart
    if (type.startsWith('ui:')) {
      return buildUiNode(
        type,
        props,
        childrenList,
        onEvent,
        buildChild: (child) => build(
          child,
          onEvent,
          rfwHost: rfwHost,
          onNavSelected: onNavSelected,
          activeTarget: activeTarget,
        ),
      );
    }
```

Add the import at the top of the file:

```dart
import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
```

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter test test/ui_kit/ui_registry_test.dart`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add lib/rfw_host/rfw_runtime_host.dart test/ui_kit/ui_registry_test.dart
git commit -m "feat(app): ui: dispatch branch in UiSurfaceTreeRenderer"
```

---

### Task 7: Typed-tree branch in the experience hop view

**Files:**
- Modify: `lib/features/experience/experience_hop_view.dart`
- Test: `test/features/experience/experience_hop_view_tree_test.dart` (new)

**Interfaces:**
- Consumes: `UiSurfaceTreeRenderer` (Task 6), `RfwRuntimeHost`, `buildInlineRfwSurface`.
- Produces: when `data['tree']` is a Map, renders it via `UiSurfaceTreeRenderer().build(...)` wrapped in `Semantics(identifier: correlationId)`; otherwise the existing RFW path.

- [ ] **Step 1: Write the failing test**

Create `test/features/experience/experience_hop_view_tree_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/features/experience/experience_hop_view.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

void main() {
  testWidgets('renders a typed ui: tree hop under the surfaceId semantics id', (tester) async {
    final data = <String, Object?>{
      'activeExperience': 'hello-world/hello-world',
      'surfaceId': 'ask',
      'tree': {
        'Type': 'ui:Screen',
        'Props': <String, Object?>{},
        'Children': [
          {'Type': 'ui:Text', 'Props': {'text': 'What\'s your name?'}, 'Children': []},
        ],
      },
    };
    await tester.pumpWidget(MaterialApp(
      home: FTheme(
        data: FThemes.zinc.light,
        child: Scaffold(
          body: ExperienceHopView(
            host: RfwRuntimeHost(),
            data: data,
            correlationId: 'ask',
            onEvent: (_, __) {},
          ),
        ),
      ),
    ));
    expect(find.text('What\'s your name?'), findsOneWidget);
    expect(
      find.bySemanticsLabel(RegExp('.*')),
      findsWidgets,
    );
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `flutter test test/features/experience/experience_hop_view_tree_test.dart`
Expected: FAIL — current view calls `buildInlineRfwSurface` (needs `data['source']`), returns `SizedBox.shrink()`; text not found.

- [ ] **Step 3: Implement**

Replace the body of `ExperienceHopView.build` in `lib/features/experience/experience_hop_view.dart`:

```dart
  @override
  Widget build(BuildContext context) {
    final tree = data['tree'];
    if (tree is Map) {
      // Typed ui: kit hop — render the node tree via the kit covers, keyed for the E2E semantics linchpin.
      return Semantics(
        identifier: correlationId,
        container: true,
        child: UiSurfaceTreeRenderer().build(
          tree.cast<String, Object?>(),
          onEvent,
          rfwHost: host,
        ),
      );
    }
    final body = buildInlineRfwSurface(
      host: host,
      data: data,
      fallbackKey: correlationId,
      defaultRootWidget: rootWidget,
      onEvent: onEvent,
      correlationId: correlationId,
      semanticsId: correlationId,
    );
    return body ?? const SizedBox.shrink();
  }
```

`UiSurfaceTreeRenderer` is already declared in `rfw_runtime_host.dart`, which this file already imports — no new import needed.

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter test test/features/experience/experience_hop_view_tree_test.dart`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add lib/features/experience/experience_hop_view.dart test/features/experience/experience_hop_view_tree_test.dart
git commit -m "feat(app): experience hop view renders typed ui: trees"
```

---

### Task 8: Wire host events (send `ExperienceStep`) + auto-fire `start` on open

**Files:**
- Modify: `lib/features/experience/experience_host_screen.dart`

**Interfaces:**
- Consumes: `buildActionEnvelope` (`lib/grpc/action_dispatch.dart`), `DigitalBrainGatewayClient`.
- Produces: real button taps fire `ExperienceStep` over `Send`; on open (target set), the host fires `ExperienceStep{eventName:"start"}` after the feed subscription is live.

- [ ] **Step 1: Add the import + client field**

At the top of `lib/features/experience/experience_host_screen.dart` add:

```dart
import 'package:digitalbrain_flutter/grpc/action_dispatch.dart';
```

In `_ExperienceHostScreenState` add fields near `_feedSub`:

```dart
  DigitalBrainGatewayClient? _client;
  bool _startFired = false;
```

In `_connect`, after `final client = DigitalBrainGatewayClient(...)` store it:

```dart
      _client = client;
```

- [ ] **Step 2: Replace `_onSurfaceEvent` and `_onCard`**

Replace the no-op `_onSurfaceEvent`:

```dart
  void _onSurfaceEvent(String name, Map<String, Object?> args) {
    final envelope = buildActionEnvelope(name, args);
    final client = _client;
    if (envelope == null || client == null) return;
    client.send(envelope).then(
      (_) {},
      onError: (Object error) => _onError(error, StackTrace.current),
    );
  }
```

Add a `_fireStart` helper:

```dart
  void _fireStart() {
    final pack = widget.pack;
    final experienceId = widget.experienceId;
    final client = _client;
    if (pack == null || experienceId == null || client == null) return;
    final envelope = gw.SynapseEnvelope()
      ..correlationId = 'start-$pack'
      ..typeName = 'ExperienceStep'
      ..payload = utf8.encode(jsonEncode({
        'pack': pack,
        'experienceId': experienceId,
        'eventName': 'start',
      }));
    client.send(envelope).then((_) {}, onError: (_) {});
  }
```

In `_onCard`, fire start once on the FIRST card (guarantees the subscription is live; the initial login surface always arrives), BEFORE the match check:

```dart
  void _onCard(gw.RfwCardEnvelope envelope) {
    if (!mounted) return;
    if (!_startFired && widget.target != null) {
      _startFired = true;
      _fireStart();
    }
    final data = _decode(envelope.dataJson);
    if (!experienceHopMatches(data, widget.target)) return;
    setState(() {
      _hopData = data;
      _hopCorrelationId = envelope.correlationId;
      _status = null;
    });
  }
```

- [ ] **Step 3: Analyze (no unit test — exercised by Task 10 E2E)**

Run: `flutter analyze lib/features/experience/experience_host_screen.dart`
Expected: No new errors. (`utf8`/`jsonEncode` come from the existing `dart:convert` import; `gw` alias already imported.)

- [ ] **Step 4: Commit**

```bash
git add lib/features/experience/experience_host_screen.dart
git commit -m "feat(app): experience host sends ExperienceStep on tap + auto-starts on open"
```

---

## Phase 3 — Hot-reload CLI

### Task 9: `dbt author <file> [--watch]`

**Files:**
- Modify: `DigitalBrain.Cli/DigitalBrain.Cli.csproj` (add gRPC client generation + packages)
- Create: `DigitalBrain.Cli/Commands/AuthorCommand.cs`
- Modify: `DigitalBrain.Cli/Program.cs` (dispatch `author` before the interactive menu)

**Interfaces:**
- Consumes: `PackSignatureVerifier.GenerateKeyPair()` + `SignPack(NeuroPack, privateKey, publicKey)` (`DigitalBrain.Core/Trust`), generated `DigitalBrainGateway.DigitalBrainGatewayClient` (from `digitalbrain.proto`, `GrpcServices="Client"`), `SynapseEnvelope`.
- Produces: `AuthorCommand.RunAsync(string file, bool watch, string gatewayUrl) -> Task<int>` — reads the file, self-signs a `NeuroPack`, sends `PublishToMarketplace` then `InstallFromMarketplace` to the running gateway; `--watch` re-runs on save.

- [ ] **Step 1: Add gRPC client to the CLI project**

In `DigitalBrain.Cli/DigitalBrain.Cli.csproj`, inside the existing `<Project>`:

```xml
  <ItemGroup>
    <PackageReference Include="Grpc.Net.Client" />
    <PackageReference Include="Google.Protobuf" />
    <PackageReference Include="Grpc.Tools" PrivateAssets="All" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="..\DigitalBrain.Kernel\Protos\digitalbrain.proto" GrpcServices="Client" Link="Protos\digitalbrain.proto" />
  </ItemGroup>
```

Add the three package versions to `Directory.Packages.props` if not already present (use Context7 / NuGet for the latest stable matching the repo's gRPC versions). Run `dotnet restore`.

- [ ] **Step 2: Write the AuthorCommand**

Create `DigitalBrain.Cli/Commands/AuthorCommand.cs`:

```csharp
using System.Text;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Trust;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Net.Client;
using Spectre.Console;

namespace DigitalBrain.Cli.Commands;

// Dev hot-loop: publish+install a pack .cs into the ALREADY-RUNNING cluster — no kernel recompile/restart.
public static class AuthorCommand
{
    public static async Task<int> RunAsync(string file, bool watch, string gatewayUrl)
    {
        if (!File.Exists(file))
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] {file}");
            return 1;
        }

        await PublishAndInstallAsync(file, gatewayUrl);

        if (!watch) return 0;

        var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        using var watcher = new FileSystemWatcher(dir, Path.GetFileName(file))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        AnsiConsole.MarkupLine($"[green]Watching[/] {file} (Ctrl+C to stop)…");
        var gate = new SemaphoreSlim(1, 1);
        watcher.Changed += async (_, _) =>
        {
            if (!await gate.WaitAsync(0)) return;
            try
            {
                await Task.Delay(150); // let the editor finish writing
                await PublishAndInstallAsync(file, gatewayUrl);
            }
            finally { gate.Release(); }
        };
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    private static async Task PublishAndInstallAsync(string file, string gatewayUrl)
    {
        var code = await File.ReadAllTextAsync(file);
        var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        const string version = "1.0.0-dev";

        var (privateKey, publicKey) = PackSignatureVerifier.GenerateKeyPair();
        var signed = PackSignatureVerifier.SignPack(
            new NeuroPack(name, version, "dev", false, 0.0, code, "Authored via dbt author"),
            privateKey, publicKey);

        using var channel = GrpcChannel.ForAddress(gatewayUrl);
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var publish = JsonSerializer.Serialize(new
        {
            PackName = name, Version = version, Code = code, OwnerId = "dev",
            IsPrivate = false, CommissionRate = 0.0, Description = "Authored via dbt author",
            AuthorPublicKeyBase64 = signed.AuthorPublicKeyBase64, SignatureBase64 = signed.SignatureBase64
        });
        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "author-pub-" + name,
            TypeName = nameof(PublishToMarketplace),
            Payload = ByteString.CopyFromUtf8(publish)
        });

        var install = JsonSerializer.Serialize(new { PackName = name, Version = version, BuyerId = "dev" });
        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "author-inst-" + name,
            TypeName = nameof(InstallFromMarketplace),
            Payload = ByteString.CopyFromUtf8(install)
        });

        AnsiConsole.MarkupLine($"[green]Published + installed[/] {name}@{version} → live");
    }
}
```

Verify `PackSignatureVerifier.GenerateKeyPair()` returns `(string privateKey, string publicKey)` (it is used identically in `DigitalBrainAppHostFixture.PublishPackAsync`). Verify the generated client namespace is `DigitalBrain.Runtime.Grpc` (per `digitalbrain.proto` `option csharp_namespace`).

- [ ] **Step 3: Dispatch from Program.cs**

At the very top of `DigitalBrain.Cli/Program.cs` `Main` (before the Spectre menu setup), add:

```csharp
if (args.Length > 0 && args[0].Equals("author", StringComparison.OrdinalIgnoreCase))
{
    var file = args.Length > 1 ? args[1] : "";
    var watch = args.Contains("--watch");
    var gatewayIndex = Array.IndexOf(args, "--gateway");
    var gateway = gatewayIndex >= 0 && gatewayIndex + 1 < args.Length
        ? args[gatewayIndex + 1]
        : Environment.GetEnvironmentVariable("DIGITALBRAIN_GATEWAY") ?? "https://localhost:7080";
    return await DigitalBrain.Cli.Commands.AuthorCommand.RunAsync(file, watch, gateway);
}
```

If `Main` is top-level statements without an explicit `return`, ensure the file's return type is `Task<int>` (top-level `return` is allowed). If it is `void`/`Task`, convert the entry to `async Task<int>`.

- [ ] **Step 4: Build the CLI**

Run: `dotnet build DigitalBrain.Cli`
Expected: Build succeeded, 0 errors. (The `Protobuf` item generates `DigitalBrainGateway.DigitalBrainGatewayClient`.)

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Cli/DigitalBrain.Cli.csproj DigitalBrain.Cli/Commands/AuthorCommand.cs DigitalBrain.Cli/Program.cs Directory.Packages.props
git commit -m "feat(cli): dbt author [--watch] hot-loop publishes+installs a pack into the live cluster"
```

---

## Phase 4 — End-to-end acceptance

### Task 10: Hello World browser E2E

**Files:**
- Create: `DigitalBrain.Tests/E2E/HelloWorldRendersE2ETests.cs`

**Interfaces:**
- Consumes: `ExperienceFlowDriver` (constructor `(DigitalBrainBrowserFixture, pack, experienceId)`, `PublishAndInstallAsync(code, description)`, `OpenAsync()`, `TriggerExperienceAsync(params (string,string)[])`, `TapAsync(eventName, params (string,string)[])`, `AssertHopRendersAsync(surfaceId)`); `HelloWorldPackSource.Code` (Task 4); `DigitalBrainBrowserFixture` (collection `DigitalBrainE2ECollection`, `E2EPrerequisites.RequireRenderE2E()`).

- [ ] **Step 1: Write the test**

Create `DigitalBrain.Tests/E2E/HelloWorldRendersE2ETests.cs` (modeled on `TravelPlanTripRendersE2ETests`):

```csharp
using DigitalBrain.Tests.E2E.Packs;
using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class HelloWorldRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task HelloWorld_asks_for_name_then_greets()
    {
        E2EPrerequisites.RequireRenderE2E();

        var driver = new ExperienceFlowDriver(_fx, pack: "hello-world", experienceId: "hello-world");
        await driver.PublishAndInstallAsync(HelloWorldPackSource.Code, description: "Hello World experience");
        await driver.OpenAsync();

        await driver.TriggerExperienceAsync();
        await driver.AssertHopRendersAsync("ask");

        await driver.TapAsync("greeting", ("name", "Alice"));
        await driver.AssertHopRendersAsync("greeting");

        await _fx.Page.Locator("text=Hello Alice!").WaitForAsync(new() { Timeout = 30_000 });
    }
}
```

- [ ] **Step 2: Build the web bundle for E2E**

The E2E renders the release web bundle. Per the E2E prerequisites, build it (from `E:\digitalbraintech\app`):

Run: `flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true`
Expected: build completes (the `--no-tree-shake-icons` flag is mandatory).

- [ ] **Step 3: Run the E2E**

From `E:\digitalbraintech\brain`:
Run: `dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~HelloWorldRendersE2ETests"`
Expected: PASS — both `ask` and `greeting` hops render full-screen; "Hello Alice!" is visible. (Screenshots land in `AppContext.BaseDirectory/e2e-screenshots`.) If skipped, the render-E2E prerequisite/env is not set; set it per `E2EPrerequisites`.

- [ ] **Step 4: Commit**

```bash
git add DigitalBrain.Tests/E2E/HelloWorldRendersE2ETests.cs
git commit -m "test(e2e): Hello World renders ask + greeting hops via the ui: kit"
```

---

## Phase 5 — Full verification ritual

### Task 11: Green-across-the-stack + live hot-loop demo

- [ ] **Step 1: brain build + fast tests**

From `E:\digitalbraintech\brain`:
Run: `dotnet build Brain.slnx -clp:ErrorsOnly && dotnet test --filter "Category!=E2E"`
Expected: Build clean; all non-E2E pass (baseline 167 + the new KitExperience/bridge tests).

- [ ] **Step 2: app analyze + tests**

From `E:\digitalbraintech\app`:
Run: `flutter analyze && flutter test`
Expected: analyze clean of NEW issues (pre-existing infos allowed); all tests pass (baseline 15 + new ui_kit/experience tests).

- [ ] **Step 3: aspire doctor**

Run (aspire MCP `doctor`, or `aspire doctor` from `E:\digitalbraintech\brain`).
Expected: all checks pass.

- [ ] **Step 4: Live hot-loop demo (intentional `aspire run`)**

From `E:\digitalbraintech\brain`: `aspire run`. In the app, open the marketplace → "hello-world" → Open → enter a name → Greet → see the greeting. Then edit a copy of the pack `.cs` (e.g. change "Hello" to "Hi"), run `dbt author <file> --watch`, save, and confirm the live surface updates with NO kernel restart. This proves the acceleration claim.

- [ ] **Step 5: Self-review + finish**

Use `superpowers:requesting-code-review` (whole-change review across both repos). Then `superpowers:finishing-a-development-branch` to decide merge/PR. Update `brain/CONTINUITY.md` with a one-paragraph slice summary.

---

## Self-Review (plan vs. spec)

**Spec coverage:**
- Fewer edit-points (1 file) → Tasks 3,4 (`KitExperience` + `HelloWorldPackSource`). ✓
- High-level ui: widgets (ForUI covers, one file each) → Tasks 5,6. ✓
- Typed nodes on the wire (Approach 1) → Tasks 1,2 (`ForExperienceHopTree` + bridge). ✓
- Open/run from marketplace (Flag B) → Task 4 (Run action `targetSurfaceKind` route) + Task 8 (auto-start). ✓
- Hot-reload (Flag C, CLI watch) → Task 9. ✓
- Hello World acceptance → Task 10. ✓
- 5 seed components (Screen/Text/TextField/Button/Panel) → Tasks 1,5. ✓
- Verification ritual → Task 11. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code. The pack-source guard test asserts no `/* TODO`. ✓

**Type consistency:** `ForExperienceHopTree` signature identical in Tasks 1/2/3; `buildUiNode` signature identical in Tasks 5/6; `UiKitButton` emits `{synapseType:'ExperienceStep', props:{pack,experienceId,eventName,...values}}` consumed by `buildActionEnvelope` (reads `synapseType` + `props`) in Task 8 → routed by `GatewayService` `ExperienceStep` case (reads `pack`/`experienceId`/`eventName` + args) — consistent. `pack==experienceId=="hello-world"` held across Tasks 3,4,8,10. ✓

**Known assumptions to verify during implementation (not placeholders):** ForUI `FTextField.onChange`/`FButton`/`FCard` exact params (Context7); `UiSurfaceTreeRenderer` is instantiable with a default ctor (same file, Task 6 confirms); CLI `Main` return-type conversion (Task 9 Step 3); gRPC package versions (Task 9 Step 1).
