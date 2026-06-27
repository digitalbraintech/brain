# Domains & Experiences — Travel Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a `travel` domain (a NeuroPack) whose single "Plan a trip" experience runs a stateful multi-hop RFW flow (start → flights → hotels → events → activities → summary), proven end-to-end up to the Flutter client.

**Architecture:** A typed-C# pack implements `IPackBehavior` and is published/installed through the existing marketplace path; on install it Roslyn-compiles into a collectible ALC and is embodied as the `GeneratedNeuron` grain, which holds the live (stateful) behavior instance across hops. Each hop returns a `UiSurface.ForRfw(...)` carrying an inline RFW DSL (using brain's already-registered `digitalbrain` widgets) in `dataJson["source"]`; the surface flows `GeneratedNeuron → HomeFeedBus → WatchHomeFeed (gRPC-Web) → Flutter`, where each hop's `props["surfaceId"]` becomes the panel id and `flt-semantics-identifier`. Taps are driven as `ExperienceStep` synapses through the gateway.

**Tech Stack:** .NET 11 (net11.0), Orleans 10.2, .NET Aspire 13.4, Reqnroll/xUnit, `Aspire.Hosting.Testing`, Microsoft.Playwright, Roslyn (Foundry), Remote Flutter Widgets (RFW).

## Global Constraints

- Target framework: **net11.0**. Central package versions in `Directory.Packages.props` — no `Version="*"`.
- The pack source references **only `DigitalBrain.Core` + BCL** (the ALC unifies Core with the host; any other reference breaks embodiment). No Kernel/test/3rd-party types in the pack file.
- **No vacuous `/// <summary>`.** Self-explanatory names; small inline comments only where genuinely non-obvious. (Project rule.)
- **Look up every external API via Context7 before writing code** (Orleans, Aspire.Hosting.Testing, Playwright, RFW). (Project rule.)
- Use **relative paths**; never leak user-profile paths.
- RFW source constants must be **LF-only** and byte-stable (CRLF guard) — build DSL with explicit `\n`, matching `app/lib/rfw_host/rfw_card_sources.dart`.
- This is **Phase 1 only** — additive types, NO `NeuroPack`→`Domain` rename here.
- Verification ritual after changes: `dotnet build`, relevant `dotnet test`, `aspire doctor`.
- E2E is opt-in: gated behind `RUN_FLUTTER_E2E` + a prebuilt Flutter web bundle; tagged `[Trait("Category","E2E")]`; single replica via `DIGITALBRAIN_KERNEL_REPLICAS=1`.

---

## File Structure

| File | Responsibility |
|---|---|
| `DigitalBrain.Core/Experience.cs` (create) | The additive `Experience` record + `ExperienceStep` synapse. |
| `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs` (modify) | RFW-kind branch prefers `props["surfaceId"]` for `RfwCard.CorrelationId`. |
| `DigitalBrain.Kernel/Gateway/GatewayService.cs` (modify) | Route an `ExperienceStep` envelope to `generated-<pack>`. |
| `DigitalBrain.Tests/E2E/Packs/TravelPack.cs` (create) | The travel pack source: models, corpora, RFW DSL builders, stateful `IPackBehavior`. Compiled for unit tests **and** embedded as the pack `Code` for E2E. |
| `DigitalBrain.Tests/Domains/TravelPackTests.cs` (create) | Fast unit tests for every hop + the stateful sequence. |
| `DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs` (create) | TestCluster test: `ExperienceStep` → embodied pack → surface on `HomeFeedBus`. |
| `DigitalBrain.Tests/E2E/TravelPackSource.cs` (create) | Reads the embedded `TravelPack.cs` text for publishing. |
| `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs` (modify) | Add `SendExperienceStepAsync(...)` helper. |
| `DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs` (create) | The gated up-to-Flutter E2E across all hops. |
| `DigitalBrain.Tests/DigitalBrain.Tests.csproj` (modify) | Embed `E2E/Packs/TravelPack.cs` as a resource (also compiled). |

Flutter (`app/`): **no code changes** — the DSL uses already-registered widgets and the existing `dataJson["source"]` path (`panel_manager.dart:89-115`). Task 7 includes a read-only verification of that path.

---

## Task 1: Core types — `Experience` + `ExperienceStep`

**Files:**
- Create: `DigitalBrain.Core/Experience.cs`
- Test: `DigitalBrain.Tests/Core/ExperienceTypesTests.cs`

**Interfaces:**
- Produces: `Experience(string ExperienceId, string Name, string Kind, string Summary, IReadOnlyDictionary<string, object?> EntryAction)`; `ExperienceStep(string Pack, string ExperienceId, string EventName, IReadOnlyDictionary<string, string> Args) : Synapse`.

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Core/ExperienceTypesTests.cs
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Core;

public class ExperienceTypesTests
{
    [Fact]
    public void ExperienceStep_carries_event_and_args_and_is_a_synapse()
    {
        var step = new ExperienceStep(
            Pack: "travel",
            ExperienceId: "plan-trip",
            EventName: "flight.selected",
            Args: new Dictionary<string, string> { ["flightId"] = "FL-001" });

        Assert.IsAssignableFrom<Synapse>(step);
        Assert.Equal(nameof(ExperienceStep), step.Type);
        Assert.Equal("flight.selected", step.EventName);
        Assert.Equal("FL-001", step.Args["flightId"]);
    }

    [Fact]
    public void Experience_holds_entry_action()
    {
        var entry = new Dictionary<string, object?> { ["synapseType"] = nameof(ExperienceStep) };
        var exp = new Experience("plan-trip", "Plan a trip", "experience", "Plan a multi-stop trip.", entry);

        Assert.Equal("plan-trip", exp.ExperienceId);
        Assert.Equal(nameof(ExperienceStep), exp.EntryAction["synapseType"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ExperienceTypesTests"`
Expected: FAIL — `Experience`/`ExperienceStep` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
// DigitalBrain.Core/Experience.cs
namespace DigitalBrain.Core;

// A named, user-facing journey a domain (NeuroPack) exposes — the first-class form of the
// rows previously hand-built in UiSurfaceLiveData.ExperiencesForPack.
[GenerateSerializer]
public record Experience(
    string ExperienceId,
    string Name,
    string Kind,
    string Summary,
    IReadOnlyDictionary<string, object?> EntryAction);

// One step of an experience flow: an entry ("start") or a tap forwarded from the client.
// Args carry the selection for that hop (flightId, hotelId, ...). Mirrors ino's RfwEventRequest.
[GenerateSerializer]
public record ExperienceStep(
    string Pack,
    string ExperienceId,
    string EventName,
    IReadOnlyDictionary<string, string> Args) : Synapse(nameof(ExperienceStep), DateTimeOffset.UtcNow);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ExperienceTypesTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Core/Experience.cs DigitalBrain.Tests/Core/ExperienceTypesTests.cs
git commit -m "Add Experience and ExperienceStep core types"
```

---

## Task 2: Bridge prefers `props["surfaceId"]` for per-hop correlation

**Why:** `GeneratedNeuron.NormalizePackOutput` (`SystemNeurons.cs:982`) sets `CorrelationId = null` on every pack output, so `UiSurfaceRfwBridge.FromUiSurface` currently falls back to a random `SynapseId` → each hop's `flt-semantics-identifier` is non-deterministic. Props survive the `with`, so prefer `props["surfaceId"]`.

**Files:**
- Modify: `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs` (the RFW-kind branch, ~lines 43-52)
- Test: `DigitalBrain.Tests/Ui/UiSurfaceRfwBridgeSurfaceIdTests.cs`

**Interfaces:**
- Consumes: `UiSurfaceRfwBridge.FromUiSurface(UiSurface, string emitter) -> RfwCard` (existing).

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Ui/UiSurfaceRfwBridgeSurfaceIdTests.cs
using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using Xunit;

namespace DigitalBrain.Tests.Ui;

public class UiSurfaceRfwBridgeSurfaceIdTests
{
    [Fact]
    public void Rfw_surface_uses_surfaceId_prop_as_correlation_when_correlation_is_null()
    {
        var surface = new UiSurface(UiSurface.RfwKind, new Dictionary<string, object?>
        {
            ["libraryName"] = "digitalbrain",
            ["rootWidget"] = "root",
            ["dataJson"] = "{\"source\":\"import digitalbrain;\"}",
            [UiSurfaceKeys.SurfaceId] = "travel-hotels",
        });

        var card = UiSurfaceRfwBridge.FromUiSurface(surface, "travel");

        Assert.Equal("travel-hotels", card.CorrelationId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UiSurfaceRfwBridgeSurfaceIdTests"`
Expected: FAIL — `CorrelationId` is the random `SynapseId`, not `"travel-hotels"`.

- [ ] **Step 3: Write minimal implementation**

In `UiSurfaceRfwBridge.FromUiSurface`, RFW-kind branch, replace the return so the surfaceId prop wins:

```csharp
// inside the `if (surface.Kind == UiSurface.RfwKind || ...)` branch
var lib = ValueOrDefault(surface, "libraryName", "digitalbrain");
var root = ValueOrDefault(surface, "rootWidget", "root");
var dataJson = surface.Props.TryGetValue("dataJson", out var dj) && dj is string s ? s
    : JsonSerializer.Serialize(surface.Props);
var correlation = surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var sid) && sid is string sidStr && sidStr.Length > 0
    ? sidStr
    : surface.CorrelationId ?? surface.SynapseId;
return new RfwCard(lib, root, dataJson) { CorrelationId = correlation };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~UiSurfaceRfwBridgeSurfaceIdTests"`
Expected: PASS. Then run the existing bridge tests to confirm no regression:
Run: `dotnet test --filter "FullyQualifiedName~UiSurfaceRfwBridge"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs DigitalBrain.Tests/Ui/UiSurfaceRfwBridgeSurfaceIdTests.cs
git commit -m "Bridge prefers surfaceId prop for per-hop RFW correlation"
```

---

## Task 3: Travel pack — models, corpora, and the start→flights→hotels hops

The pack is one Core-only source file. Task 3 builds it through the hotels hop; Task 4 finishes it. It is compiled into the test assembly (fast unit tests) and embedded for E2E (Task 6).

**Files:**
- Create: `DigitalBrain.Tests/E2E/Packs/TravelPack.cs`
- Test: `DigitalBrain.Tests/Domains/TravelPackTests.cs`

**Interfaces:**
- Produces: `public sealed class TravelPackBehavior : IPackBehavior` with stateful fields; emits `UiSurface` (RFW kind) per hop. Hop ids: `travel-intro`, `travel-hotels`, `travel-events`, `travel-activities`, `travel-summary`. Handles `ExperienceStep` with `EventName` in {`start`,`flight.selected`,`hotel.selected`,`event.selected`,`events.skipped`,`activity.selected`}.
- Consumes: `Experience`, `ExperienceStep`, `UiSurface`, `UiSurfaceKeys`, `IPackBehavior`, `PackManifest`, `SynapseType` from Task 1 + Core.

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Domains/TravelPackTests.cs
using DigitalBrain.Core;
using DigitalBrain.Tests.E2E.Packs;
using Xunit;

namespace DigitalBrain.Tests.Domains;

public class TravelPackTests
{
    static ExperienceStep Step(string ev, params (string, string)[] args) =>
        new("travel", "plan-trip", ev, args.ToDictionary(a => a.Item1, a => a.Item2));

    static UiSurface OnlySurface(IReadOnlyList<Synapse> outputs)
    {
        var surface = outputs.OfType<UiSurface>().Single();
        Assert.Equal(UiSurface.RfwKind, surface.Kind);
        return surface;
    }

    static string SurfaceId(UiSurface s) => (string)s.Props[UiSurfaceKeys.SurfaceId]!;
    static string Source(UiSurface s)
    {
        var json = (string)s.Props["dataJson"]!;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("source").GetString()!;
    }

    [Fact]
    public void Start_emits_intro_surface_with_weather_and_flights()
    {
        var pack = new TravelPackBehavior();
        var outputs = pack.Handle(Step("start", ("prompt", "plan a trip to Bali next month")));

        var surface = OnlySurface(outputs);
        Assert.Equal("travel-intro", SurfaceId(surface));
        var src = Source(surface);
        Assert.Contains("WEATHER", src);
        Assert.Contains("Bali", src);
        Assert.Contains("ExperienceStep", src);      // flight Select button wires a step
        Assert.Contains("flight.selected", src);
    }

    [Fact]
    public void Flight_selected_emits_hotels_surface()
    {
        var pack = new TravelPackBehavior();
        pack.Handle(Step("start", ("prompt", "plan a trip to Bali next month")));
        var outputs = pack.Handle(Step("flight.selected", ("flightId", "FL-001")));

        var surface = OnlySurface(outputs);
        Assert.Equal("travel-hotels", SurfaceId(surface));
        Assert.Contains("HOTEL", Source(surface));
        Assert.Contains("hotel.selected", Source(surface));
    }

    [Fact]
    public void Manifest_declares_experience_step_handling()
    {
        var pack = new TravelPackBehavior();
        Assert.Contains(pack.GetManifest().HandledSynapseTypes, t => t.Value == nameof(ExperienceStep));
        Assert.True(pack.CanHandle(Step("start")));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~TravelPackTests"`
Expected: FAIL — `TravelPackBehavior` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DigitalBrain.Tests/E2E/Packs/TravelPack.cs
// Travel domain pack. References ONLY DigitalBrain.Core + BCL so it embodies cleanly in a
// collectible ALC. Compiled into the test assembly for fast unit tests AND embedded as the
// pack Code string for the E2E publish (see TravelPackSource).
using System.Text;
using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Tests.E2E.Packs;

public sealed record TravelFlight(string Id, string Airline, string From, string To, int Price, string Duration);
public sealed record TravelHotel(string Id, string Name, string City, int Price, double Rating);
public sealed record TravelEvent(string Id, string Title, string DateLabel, string Venue);
public sealed record TravelActivity(string Id, string Name, string Category, double Rating, string WeatherBadge);
public sealed record TravelWeather(string Destination, string Month, string Season, int AvgTempC, int RainPct);

internal static class TravelCorpus
{
    public static TravelWeather Weather(string destination, string month) =>
        new(destination, month, "dry", 28, 15);

    public static IReadOnlyList<TravelFlight> Flights() =>
    [
        new("FL-001", "Singapore Airlines", "London", "Bali", 850, "15h 45m"),
        new("FL-002", "Qatar Airways", "London", "Bali", 720, "18h 00m"),
    ];

    public static IReadOnlyList<TravelHotel> Hotels() =>
    [
        new("H-001", "Bambu Indah", "Ubud", 280, 4.7),
        new("H-002", "Como Uma Ubud", "Ubud", 420, 4.8),
    ];

    public static IReadOnlyList<TravelEvent> Events() =>
    [
        new("EV-001", "Gamelan Night at Ubud Palace", "Sat, Jun 14", "Ubud Palace"),
        new("EV-002", "Rice Terrace Sunrise Walk", "Sun, Jun 15", "Tegalalang"),
    ];

    public static IReadOnlyList<TravelActivity> Activities() =>
    [
        new("AC-001", "Tegalalang Rice Terraces", "Nature", 4.6, "Sunny day pick"),
        new("AC-002", "Agung Rai Museum of Art", "Museum", 4.6, "Cool off indoors"),
    ];

    public static TravelFlight? Flight(string? id) => Flights().FirstOrDefault(f => f.Id == id);
    public static TravelHotel? Hotel(string? id) => Hotels().FirstOrDefault(h => h.Id == id);
    public static TravelEvent? Event(string? id) => Events().FirstOrDefault(e => e.Id == id);
    public static TravelActivity? Activity(string? id) => Activities().FirstOrDefault(a => a.Id == id);

    public static string Destination(string prompt)
    {
        var marker = " to ";
        var i = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "your destination";
        var rest = prompt[(i + marker.Length)..].Trim();
        var word = rest.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "your destination";
        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }
}

// Builds inline RFW DSL using brain's registered `digitalbrain` widgets. The DSL ships inside
// dataJson["source"]; brain's panel_manager reads it (panel_manager.dart:97).
internal static class TravelCards
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static string StepEvent(string ev, string argKey, string argValueExpr) =>
        $"event \"step\" {{ synapseType: \"ExperienceStep\", pack: \"travel\", experienceId: \"plan-trip\", eventName: \"{ev}\", {argKey}: {argValueExpr} }}";

    public static UiSurface Intro(TravelWeather w, IReadOnlyList<TravelFlight> flights)
    {
        var b = new StringBuilder();
        b.Append("import digitalbrain;\n");
        b.Append("widget root = VStack(gap: 12.0, children: [\n");
        b.Append("  Panel(radius: 20.0, padding: 18.0, child: VStack(gap: 6.0, cross: \"start\", children: [\n");
        b.Append("    SectionLabel(text: \"WEATHER\"),\n");
        b.Append("    Text(text: data.weather.headline, variant: \"heading\"),\n");
        b.Append("  ])),\n");
        b.Append("  ...for f in data.flights:\n");
        b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
        b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: f.airline, variant: \"title\"), Text(text: f.duration, variant: \"dim\") ]),\n");
        b.Append($"      Button(label: \"Select\", onTap: {StepEvent("flight.selected", "flightId", "f.flightId")}),\n");
        b.Append("    ])),\n");
        b.Append("]);\n");

        var data = new
        {
            source = b.ToString(),
            weather = new { headline = $"{w.Month} in {w.Destination}: {w.Season} season, ~{w.AvgTempC}°C, {w.RainPct}% rain." },
            flights = flights.Select(f => new { airline = f.Airline, duration = f.Duration, flightId = f.Id }),
        };
        return Surface("travel-intro", data);
    }

    public static UiSurface Hotels(IReadOnlyList<TravelHotel> hotels)
    {
        var b = new StringBuilder();
        b.Append("import digitalbrain;\n");
        b.Append("widget root = VStack(gap: 12.0, children: [\n");
        b.Append("  Panel(radius: 20.0, padding: 18.0, child: SectionLabel(text: \"HOTELS\")),\n");
        b.Append("  ...for h in data.hotels:\n");
        b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
        b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: h.name, variant: \"title\"), Stars(value: h.rating) ]),\n");
        b.Append($"      Button(label: \"Select\", onTap: {StepEvent("hotel.selected", "hotelId", "h.hotelId")}),\n");
        b.Append("    ])),\n");
        b.Append("]);\n");

        var data = new { source = b.ToString(), hotels = hotels.Select(h => new { name = h.Name, rating = h.Rating, hotelId = h.Id }) };
        return Surface("travel-hotels", data);
    }

    public static UiSurface Surface(string surfaceId, object data)
    {
        var props = new Dictionary<string, object?>
        {
            ["libraryName"] = "digitalbrain",
            ["rootWidget"] = "root",
            ["dataJson"] = JsonSerializer.Serialize(data, Json),
            [UiSurfaceKeys.SurfaceId] = surfaceId,
            [UiSurfaceKeys.Emitter] = "travel",
            [UiSurfaceKeys.Title] = "Plan a trip",
        };
        return new UiSurface(UiSurface.RfwKind, props);
    }
}

public sealed class TravelPackBehavior : IPackBehavior
{
    string _destination = "your destination";
    string _month = "this season";
    TravelFlight? _flight;
    TravelHotel? _hotel;
    TravelEvent? _event;

    public string Respond(string input) => $"travel: {input}";

    public PackManifest GetManifest() => new([new SynapseType(nameof(ExperienceStep))]);

    public bool CanHandle(Synapse synapse) => synapse is ExperienceStep;

    public IReadOnlyList<Synapse> Handle(Synapse synapse)
    {
        if (synapse is not ExperienceStep step) return [];

        switch (step.EventName)
        {
            case "start":
                var prompt = step.Args.GetValueOrDefault("prompt", "plan a trip to Bali next month");
                _destination = TravelCorpus.Destination(prompt);
                _month = "next month";
                return [TravelCards.Intro(TravelCorpus.Weather(_destination, _month), TravelCorpus.Flights())];

            case "flight.selected":
                _flight = TravelCorpus.Flight(step.Args.GetValueOrDefault("flightId"));
                return [TravelCards.Hotels(TravelCorpus.Hotels())];

            default:
                return [];
        }
    }
}
```

- [ ] **Step 4: Wire the file into the test project (compile + embed)**

In `DigitalBrain.Tests/DigitalBrain.Tests.csproj`, add (the default glob already compiles it; this also embeds it):

```xml
<ItemGroup>
  <EmbeddedResource Include="E2E/Packs/TravelPack.cs" LogicalName="TravelPack.cs" />
</ItemGroup>
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~TravelPackTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Tests/E2E/Packs/TravelPack.cs DigitalBrain.Tests/Domains/TravelPackTests.cs DigitalBrain.Tests/DigitalBrain.Tests.csproj
git commit -m "Add travel pack: start and flight hops with unit tests"
```

---

## Task 4: Travel pack — events, activities, summary hops + full sequence test

**Files:**
- Modify: `DigitalBrain.Tests/E2E/Packs/TravelPack.cs` (add 3 card builders + 4 switch cases)
- Modify: `DigitalBrain.Tests/Domains/TravelPackTests.cs` (add hop + sequence tests)

**Interfaces:**
- Consumes/Produces: extends `TravelPackBehavior.Handle` to emit `travel-events`, `travel-activities`, `travel-summary`.

- [ ] **Step 1: Write the failing test**

```csharp
// append to DigitalBrain.Tests/Domains/TravelPackTests.cs
[Fact]
public void Full_sequence_walks_intro_to_summary()
{
    var pack = new TravelPackBehavior();
    Assert.Equal("travel-intro",      SurfaceId(OnlySurface(pack.Handle(Step("start", ("prompt", "plan a trip to Bali next month"))))));
    Assert.Equal("travel-hotels",     SurfaceId(OnlySurface(pack.Handle(Step("flight.selected", ("flightId", "FL-001"))))));
    Assert.Equal("travel-events",     SurfaceId(OnlySurface(pack.Handle(Step("hotel.selected", ("hotelId", "H-001"))))));
    Assert.Equal("travel-activities", SurfaceId(OnlySurface(pack.Handle(Step("event.selected", ("eventId", "EV-001"))))));
    var summary = OnlySurface(pack.Handle(Step("activity.selected", ("activityId", "AC-001"))));
    Assert.Equal("travel-summary", SurfaceId(summary));
    Assert.Contains("Bali", Source(summary));
    Assert.Contains("Singapore Airlines", Source(summary)); // FL-001 airline carried through state
}

[Fact]
public void Events_can_be_skipped_and_still_reach_activities()
{
    var pack = new TravelPackBehavior();
    pack.Handle(Step("start", ("prompt", "plan a trip to Bali next month")));
    pack.Handle(Step("flight.selected", ("flightId", "FL-001")));
    pack.Handle(Step("hotel.selected", ("hotelId", "H-001")));
    var activities = OnlySurface(pack.Handle(Step("events.skipped")));
    Assert.Equal("travel-activities", SurfaceId(activities));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~TravelPackTests.Full_sequence_walks_intro_to_summary"`
Expected: FAIL — `hotel.selected`/`event.selected`/`activity.selected` return `[]`, so `OnlySurface` throws on "Sequence contains no elements".

- [ ] **Step 3: Write minimal implementation**

Add three builders to `TravelCards`:

```csharp
public static UiSurface Events(IReadOnlyList<TravelEvent> events)
{
    var b = new StringBuilder();
    b.Append("import digitalbrain;\n");
    b.Append("widget root = VStack(gap: 12.0, children: [\n");
    b.Append("  Panel(radius: 20.0, padding: 18.0, child: SectionLabel(text: \"EVENTS\")),\n");
    b.Append("  ...for e in data.events:\n");
    b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
    b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: e.title, variant: \"title\"), Text(text: e.dateLabel, variant: \"dim\") ]),\n");
    b.Append($"      Button(label: \"Select\", onTap: {StepEvent("event.selected", "eventId", "e.eventId")}),\n");
    b.Append("    ])),\n");
    b.Append($"  Button(label: \"Skip events\", onTap: event \"step\" {{ synapseType: \"ExperienceStep\", pack: \"travel\", experienceId: \"plan-trip\", eventName: \"events.skipped\" }}),\n");
    b.Append("]);\n");
    var data = new { source = b.ToString(), events = events.Select(e => new { title = e.Title, dateLabel = e.DateLabel, eventId = e.Id }) };
    return Surface("travel-events", data);
}

public static UiSurface Activities(IReadOnlyList<TravelActivity> activities)
{
    var b = new StringBuilder();
    b.Append("import digitalbrain;\n");
    b.Append("widget root = VStack(gap: 12.0, children: [\n");
    b.Append("  Panel(radius: 20.0, padding: 18.0, child: SectionLabel(text: \"ACTIVITIES\")),\n");
    b.Append("  ...for a in data.activities:\n");
    b.Append("    Panel(radius: 16.0, padding: 14.0, child: HStack(between: true, children: [\n");
    b.Append("      VStack(gap: 4.0, cross: \"start\", children: [ Text(text: a.name, variant: \"title\"), Badge(text: a.weatherBadge, tone: \"teal\") ]),\n");
    b.Append($"      Button(label: \"Select\", onTap: {StepEvent("activity.selected", "activityId", "a.activityId")}),\n");
    b.Append("    ])),\n");
    b.Append("]);\n");
    var data = new { source = b.ToString(), activities = activities.Select(a => new { name = a.Name, weatherBadge = a.WeatherBadge, activityId = a.Id }) };
    return Surface("travel-activities", data);
}

public static UiSurface Summary(string destination, string flightAirline, string hotelName, string eventTitle, string activityName)
{
    var b = new StringBuilder();
    b.Append("import digitalbrain;\n");
    b.Append("widget root = Panel(radius: 20.0, padding: 18.0, child: VStack(gap: 8.0, cross: \"start\", children: [\n");
    b.Append("  SectionLabel(text: \"TRIP SUMMARY\"),\n");
    b.Append("  Text(text: data.destination, variant: \"heading\"),\n");
    b.Append("  KeyValue(label: \"Flight\", value: data.flight),\n");
    b.Append("  KeyValue(label: \"Hotel\", value: data.hotel),\n");
    b.Append("  KeyValue(label: \"Event\", value: data.event),\n");
    b.Append("  KeyValue(label: \"Activity\", value: data.activity),\n");
    b.Append("]));\n");
    var data = new
    {
        source = b.ToString(),
        destination,
        flight = flightAirline,
        hotel = hotelName,
        @event = eventTitle,
        activity = activityName,
    };
    return Surface("travel-summary", data);
}
```

Extend `TravelPackBehavior.Handle`'s switch (replace the `default` with the remaining cases):

```csharp
case "hotel.selected":
    _hotel = TravelCorpus.Hotel(step.Args.GetValueOrDefault("hotelId"));
    return [TravelCards.Events(TravelCorpus.Events())];

case "event.selected":
    _event = TravelCorpus.Event(step.Args.GetValueOrDefault("eventId"));
    return [TravelCards.Activities(TravelCorpus.Activities())];

case "events.skipped":
    _event = null;
    return [TravelCards.Activities(TravelCorpus.Activities())];

case "activity.selected":
    var activity = TravelCorpus.Activity(step.Args.GetValueOrDefault("activityId"));
    return
    [
        TravelCards.Summary(
            _destination,
            _flight?.Airline ?? "(none)",
            _hotel?.Name ?? "(none)",
            _event?.Title ?? "(skipped)",
            activity?.Name ?? "(none)")
    ];

default:
    return [];
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~TravelPackTests"`
Expected: PASS (all, including the two new tests).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/E2E/Packs/TravelPack.cs DigitalBrain.Tests/Domains/TravelPackTests.cs
git commit -m "Complete travel pack hops: events, activities, summary"
```

---

## Task 5: Dispatch `ExperienceStep` through embodiment to HomeFeedBus (TestCluster)

Proves the kernel path: an installed+embodied travel pack, fired an `ExperienceStep`, emits the right surface onto `HomeFeedBus`. This is the in-cluster proof that does not need a browser.

**Files:**
- Create: `DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs`

**Interfaces:**
- Consumes: existing `TestCluster` harness used by `HomeFeedBusTests`/`ChatNeuronTests`; `IGeneratedNeuron`, `HomeFeedBus`, `NeuroPack`, `NeuroPackInstalled`. Pack source via `TravelPackSource.Read()` (created in Task 6 — for this task, inline the source string using `new TravelPackBehavior()`-equivalent is NOT possible across ALC, so read the embedded resource; if Task 6 not yet done, implement `TravelPackSource` first).

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs
using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Tests.E2E;        // TravelPackSource
using Xunit;

namespace DigitalBrain.Tests.Kernel;

[Collection(nameof(KernelClusterCollection))]   // reuse the existing TestCluster collection
public class ExperienceStepDispatchTests(KernelClusterFixture fixture)
{
    readonly KernelClusterFixture _fx = fixture;

    [Fact]
    public async Task ExperienceStep_start_emits_intro_surface_to_home_feed()
    {
        var pack = new NeuroPack("travel", "1.0", Code: TravelPackSource.Read(), OwnerId: "test",
            IsPrivate: false, CommissionRate: 0, Description: "travel domain");

        var generated = _fx.Cluster.GrainFactory.GetGrain<IGeneratedNeuron>("generated-travel");
        await generated.DeliverAsync(new NeuroPackInstalled(pack));   // embodies the pack

        var bus = _fx.Services.GetRequiredService<HomeFeedBus>();
        using var sub = bus.Subscribe();

        await generated.FireAsync(new ExperienceStep("travel", "plan-trip", "start",
            new Dictionary<string, string> { ["prompt"] = "plan a trip to Bali next month" }));

        var card = await ReadOneAsync(sub.Reader, TimeSpan.FromSeconds(10));
        Assert.Equal("travel-intro", card.CorrelationId);
        Assert.Contains("WEATHER", card.DataJson);
    }

    static async Task<RfwCard> ReadOneAsync(System.Threading.Channels.ChannelReader<RfwCard> reader, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var card in reader.ReadAllAsync(cts.Token))
            return card;
        throw new TimeoutException("No RfwCard arrived.");
    }
}
```

> NOTE: match the exact fixture/collection names this repo uses for TestCluster tests. Confirm by opening `DigitalBrain.Tests/Ui/HomeFeedBusTests.cs` and copying its `[Collection(...)]` attribute, fixture type, and how it resolves `HomeFeedBus` (cluster service provider). Adjust `KernelClusterCollection`/`KernelClusterFixture`/`_fx.Services` to the real names before running.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ExperienceStepDispatchTests"`
Expected: FAIL — either `TravelPackSource` missing (do Task 6 Step 3 first) or no card arrives because routing isn't proven yet.

- [ ] **Step 3: Make it pass**

The production path already supports this (Task 2 bridge + `BroadcastPackSurface` at `SystemNeurons.cs:996` + pack manifest declaring `ExperienceStep`). If the card does not arrive, debug with the systematic-debugging skill — likely causes: (a) `CanHandle` returns false (manifest), (b) the cluster's `HomeFeedBus` is a different instance than the one subscribed (resolve the same singleton the grain uses), (c) embodiment failed (assert `PackEmission` "pack-error" absent on the timeline). No new production code should be required; if it is, it belongs in `GeneratedNeuron`/registration and must be covered here.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ExperienceStepDispatchTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs
git commit -m "Prove ExperienceStep dispatch to HomeFeedBus in TestCluster"
```

---

## Task 6: Gateway routing + embedded pack source + fixture helper

**Files:**
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs` (add an `ExperienceStep` case in `Send`)
- Create: `DigitalBrain.Tests/E2E/TravelPackSource.cs`
- Modify: `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs` (add `SendExperienceStepAsync`)

**Interfaces:**
- Produces: `TravelPackSource.Read() -> string`; `DigitalBrainAppHostFixture.SendExperienceStepAsync(string pack, string experienceId, string eventName, IReadOnlyDictionary<string,string> args)`.
- Consumes: gateway `DigitalBrainGatewayClient.SendAsync(SynapseEnvelope)`; `IGeneratedNeuron`.

- [ ] **Step 1: Add the gateway route (with the existing switch style)**

In `GatewayService.Send`, add before the resolver fallback:

```csharp
if (request.TypeName == nameof(ExperienceStep) || request.TypeName.Contains("ExperienceStep", StringComparison.OrdinalIgnoreCase))
{
    var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
    var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(payloadStr) ?? new();
    var pack = p.GetValueOrDefault("pack", "");
    var experienceId = p.GetValueOrDefault("experienceId", "");
    var eventName = p.GetValueOrDefault("eventName", "start");
    var args = p.Where(kv => kv.Key is not ("pack" or "experienceId" or "eventName" or "synapseType"))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
    var generated = grains.GetGrain<IGeneratedNeuron>("generated-" + pack.ToLowerInvariant());
    await generated.FireAsync(new ExperienceStep(pack, experienceId, eventName, args));
    return request;
}
```

- [ ] **Step 2: Create the embedded-source reader**

```csharp
// DigitalBrain.Tests/E2E/TravelPackSource.cs
using System.Reflection;

namespace DigitalBrain.Tests.E2E;

public static class TravelPackSource
{
    public static string Read()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("TravelPack.cs")
            ?? throw new InvalidOperationException("Embedded TravelPack.cs not found; check the EmbeddedResource LogicalName.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 3: Add the fixture helper**

```csharp
// add to DigitalBrainAppHostFixture
public async Task SendExperienceStepAsync(string pack, string experienceId, string eventName, IReadOnlyDictionary<string, string>? args = null)
{
    using var channel = CreateGatewayGrpcChannel();
    var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

    var payload = new Dictionary<string, string>(args ?? new Dictionary<string, string>())
    {
        ["pack"] = pack,
        ["experienceId"] = experienceId,
        ["eventName"] = eventName,
    };

    await client.SendAsync(new SynapseEnvelope
    {
        CorrelationId = "e2e-step-" + eventName,
        TypeName = nameof(ExperienceStep),
        Payload = ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(payload))
    });
}
```

- [ ] **Step 4: Verify build + the embedded resource resolves**

Run: `dotnet build`
Expected: succeeds. Then run the Task 5 test (which calls `TravelPackSource.Read()`):
Run: `dotnet test --filter "FullyQualifiedName~ExperienceStepDispatchTests"`
Expected: PASS (confirms the embedded `TravelPack.cs` text compiles via Roslyn during embodiment).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Tests/E2E/TravelPackSource.cs DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs
git commit -m "Route ExperienceStep through gateway; embed travel pack source"
```

---

## Task 7: End-to-end — publish → embody → multi-hop → Flutter render

**Files:**
- Create: `DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs`

**Interfaces:**
- Consumes: `DigitalBrainBrowserFixture` (Playwright), `DigitalBrainAppHostFixture.PublishPackAsync/InstallPackAsync/SendExperienceStepAsync`, `E2EPrerequisites.RequireRenderE2E()`, `TravelPackSource.Read()`.

- [ ] **Step 1: Read-only verification of the Flutter source path**

Open `app/lib/features/canvas/panel/panel_manager.dart:89-115` and confirm `_surfaceFromEnvelope` reads `data['source']` and uses `env.correlationId` as the panel id. Open `app/lib/rfw_host/digitalbrain_rfw_library.dart` and confirm `VStack`, `HStack`, `Panel`, `Text`, `Badge`, `SectionLabel`, `Stars`, `KeyValue`, `Button` are registered. (They are, per `_widgets`.) No code change expected; if `Button`/`Stars`/`KeyValue` are absent, that's a follow-up Flutter task — record it and proceed (the E2E asserts render + per-hop id, which does not depend on the leaf widget set as long as the root `VStack/Panel/Text` render).

- [ ] **Step 2: Write the E2E test**

```csharp
// DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs
using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class TravelPlanTripRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task PlanTrip_walks_hops_and_each_renders_in_flutter()
    {
        E2EPrerequisites.RequireRenderE2E();

        await _fx.PublishPackAsync("travel", "1.0", code: TravelPackSource.Read(),
            description: "Travel domain — Plan a trip experience");
        await _fx.InstallPackAsync("travel", "1.0", buyer: "e2e-travel");

        await _fx.Page.GotoAsync(_fx.GatewayHttpsUrl, new() { WaitUntil = WaitUntilState.Load });

        await Step("start",            "travel-intro",      ("prompt", "plan a trip to Bali next month"));
        await Step("flight.selected",  "travel-hotels",     ("flightId", "FL-001"));
        await Step("hotel.selected",   "travel-events",     ("hotelId", "H-001"));
        await Step("event.selected",   "travel-activities", ("eventId", "EV-001"));
        await Step("activity.selected","travel-summary",    ("activityId", "AC-001"));

        async Task Step(string eventName, string surfaceId, params (string, string)[] args)
        {
            await _fx.SendExperienceStepAsync("travel", "plan-trip", eventName,
                args.ToDictionary(a => a.Item1, a => a.Item2));
            var node = _fx.Page.Locator($"[flt-semantics-identifier=\"{surfaceId}\"]");
            await node.WaitForAsync(new() { Timeout = 30_000 });
            Assert.Equal(1, await node.CountAsync());
            await _fx.Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"e2e-travel-{surfaceId}.png") });
        }
    }
}
```

- [ ] **Step 3: Run the E2E (opted-in)**

Build the web bundle once (per `E2EPrerequisites`), then:

```bash
# from app/: flutter build web --release
# set RUN_FLUTTER_E2E + DIGITALBRAIN_WEBROOT per E2EPrerequisites, then:
dotnet test --filter "FullyQualifiedName~TravelPlanTripRendersE2ETests"
```

Expected: PASS — five hops each render with their `flt-semantics-identifier`. If a hop fails to render, use systematic-debugging: check the kernel logs (`list_console_logs`) for the emitted `RfwCardEnvelope.CorrelationId`, confirm the `dataJson["source"]` parsed (no RFW parse error in browser console), and that the prior hop's state carried (Task 4 sequence test already proves the C# side).

- [ ] **Step 4: Verify the default suite is unaffected**

Run: `dotnet test --filter "Category!=E2E"`
Expected: PASS (E2E stays opt-in; everything else green).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs
git commit -m "Add travel Plan-a-trip up-to-Flutter E2E across all hops"
```

---

## Task 8: Aspire verification + wrap-up

**Files:** none (verification only).

- [ ] **Step 1: Full build + non-E2E suite**

Run: `dotnet build && dotnet test --filter "Category!=E2E"`
Expected: build succeeds; all non-E2E tests pass.

- [ ] **Step 2: Aspire doctor**

Run: `aspire doctor` (or the aspire MCP `doctor` tool)
Expected: no blocking issues.

- [ ] **Step 3: Request code review**

Use the `superpowers:requesting-code-review` skill (and the project's code-review rule) over the diff. Focus: naming self-explanatory (no vacuous summaries), the pack references only Core, no `NeuroPack`→`Domain` rename leaked into Phase 1.

- [ ] **Step 4: Update CONTINUITY.md**

Add a dated entry to `brain/CONTINUITY.md` summarizing: Experience/ExperienceStep added; travel pack + multi-hop E2E; bridge surfaceId fix; Phase 2 (rename) still pending.

```bash
git add brain/CONTINUITY.md
git commit -m "Log travel-slice consolidation in CONTINUITY"
```

---

## Self-Review

**Spec coverage:**
- Experience first-class type → Task 1. ✓
- ExperienceStep tap carrier (spec §3.2) → Task 1 + Task 6. ✓
- Travel pack = NeuroPack with one "Plan a trip" experience, stateful, mock corpora (spec §3.2) → Tasks 3–4. ✓
- RFW emission via brain's UiSurface, inline source (spec §3.2) → Tasks 3–4 (`TravelCards`). ✓
- Per-hop semantics id / surfaceId threading (spec §7 risk 2) → Task 2 + Task 7. ✓
- Tap path / ExperienceStep routing (spec §7 risk 1) → Task 5 (cluster) + Task 6 (gateway). ✓
- RFW card libraries render on Flutter (spec §7 risk 3) → resolved by using registered `digitalbrain` widgets; Task 7 Step 1 verifies. ✓
- E2E harness adapted onto existing fixture (spec §3.3) → Task 6 helper + Task 7. ✓
- Gating / single-replica / trait conventions (spec §6) → Task 7 + Global Constraints. ✓
- NOT doing: no rename, no silo-per-domain, no real APIs, no per-user state, no cross-origin (spec §5) → honored; Phase 2 deferred. ✓

**Placeholder scan:** No TBD/TODO. The two "match the repo's exact fixture name" notes (Task 5) are explicit verification steps against named files, not deferred logic.

**Type consistency:** `ExperienceStep(Pack, ExperienceId, EventName, Args)` and `Experience(ExperienceId, Name, Kind, Summary, EntryAction)` are used identically across Tasks 1/3/5/6. Surface ids (`travel-intro/hotels/events/activities/summary`) match between the pack (Tasks 3–4), the bridge (Task 2), and the E2E assertions (Task 7). `TravelPackSource.Read()` produced in Task 6, consumed in Tasks 5 and 7.
