# Domains & Experiences — Travel Vertical Slice (Design)

Date: 2026-06-28
Status: Proposed — awaiting review
Scope: `brain/` (canonical backend) + `app/` (Flutter client), harvesting from `Projects/ino`

## 1. Goal

Make **Domain** and **Experience** first-class concepts a developer authors, and prove
the whole vision through **one vertical slice** — a `travel` domain with a single
"Plan a trip" experience — tested **end-to-end up to Flutter**.

Once the slice is green, replicate the pattern to other domains and run the
terminology rename across the codebase.

### Vocabulary (the model)

| Term | Meaning | Maps to today |
|---|---|---|
| **Domain** | The publishable / installable unit a developer ships. | `NeuroPack` (renamed in Phase 2). |
| **Experience** | A named, user-facing journey inside a domain — a screen or a multi-step flow rendered in Flutter. | The ad-hoc `experiences` dictionary rows built in `UiSurfaceLiveData.ExperiencesForPack` (`DigitalBrain.Core/UiSurfaces.cs:990`), promoted to a real type. |
| **Neuron / Synapse** | Unchanged core primitives. | Unchanged. |

One Domain ships N Experiences. The implementation of an Experience is one-or-more
neuron handlers; the Experience itself is the journey (name + entry action).

## 2. Key findings that shape this design

1. **"Experience" already exists in brain, informally.** `ExperiencesForPack` already
   produces rows shaped `{ experienceId, bundleName, name, kind, summary, action }`
   and dispatches via the `ExperienceUsed` synapse. We are promoting an existing
   ad-hoc shape to a first-class type, not inventing vocabulary. The current code
   *infers* a pack's experiences with a hardcoded `if (pack.Name.Equals(...))` switch
   (`UiSurfaces.cs:990–1170`); we replace that with packs **declaring** their experiences.

2. **`Projects/ino` already implemented the richer pattern and the E2E harness.**
   - ino organizes code as `domains/<name>/` and already uses an `IDomain` marker.
   - ino's `TripPlanner` is a proven multi-hop RFW flow (weather+flights → hotels →
     events → activities → summary) driven by `IRfwEventHandler` + a `RfwEvent` gRPC
     call; it holds flow state in the grain keyed by conversation id.
   - ino's `NeuronE2ETest<T>` base + `InoBrowserFixture` (Aspire.Hosting.Testing +
     Playwright + gRPC) drive the full flow and assert per-hop content.
   - **We harvest the pattern and the test ergonomics, NOT ino's architecture.**
     ino runs a silo-per-domain; brain runs a pack compiled into a collectible ALC and
     embodied as a `GeneratedNeuron`. The slice expresses ino's flow in brain's runtime.

3. **brain's embodiment already supports stateful multi-hop flows with no new machinery.**
   `GeneratedNeuron` (`SystemNeurons.cs:860`) holds the live `IPackBehavior` instance in
   `_embodied` for the grain's whole activation and dispatches each synapse to
   `_embodied.Handle(...)` (`:957`). A pack class with fields (`_selectedFlight`, …)
   keeps state across taps automatically — the same shape as ino's `TripPlanner`.
   *Caveat:* the grain is keyed per pack (`generated-<packname>`), so flow state is
   per-install, not per-user. Acceptable for the single-user slice; multi-user
   (correlationId-keyed sub-state) is an explicit follow-up.

4. **Two naming collisions exist for the Phase 2 rename:**
   - `app/lib/features/live/graph/domain_palette.dart` — `domain` there means a visual
     cluster-color category. Rename to `cluster_palette.dart` before reusing "domain".
   - `ExperienceUsed` synapse — keep as-is; it becomes *more* correct once Experience is
     first-class ("an experience was used").

## 3. Architecture

### 3.1 New Core type: `Experience` (additive, no rename)

In `DigitalBrain.Core`:

```csharp
[GenerateSerializer]
public record Experience(
    string ExperienceId,
    string Name,
    string Kind,        // "experience" | "app"
    string Summary,
    IReadOnlyDictionary<string, object?> EntryAction); // the SynapseAction that triggers it
```

- A **Domain** (still `NeuroPack` until Phase 2) gains an optional declared experiences
  list. Packs that declare experiences replace the hardcoded name-switch in
  `ExperiencesForPack`; unknown packs keep the existing generic fallback rows so nothing
  regresses.
- `UiSurfaceLiveData.ExperiencesForPack` is refactored to project declared `Experience`
  records into the existing surface dictionaries — same wire shape out, less ad-hoc code in.

### 3.2 The travel domain (a NeuroPack)

A new typed-C# pack published & installed through the normal marketplace path
(`PublishToMarketplace` → `InstallFromMarketplace` → embody). It declares **one
Experience: "Plan a trip"** and implements a stateful `IPackBehavior`:

- **Entry** (`ExperienceUsed`/trigger, action `travel:plan-trip`): parse destination/month
  from the prompt, build the intro surface (weather summary + flight cards) via
  `UiSurface.ForRfw(...)`, return it.
- **Hops** (taps): handle a per-step tap carrying `eventName` + args
  (`flight.selected {flightId}`, `hotel.selected {hotelId}`, `event.selected {eventId}`
  / `events.skipped`, `activity.selected {activityId}`). Each mutates the pack's fields
  and returns the next `UiSurface`. The final hop returns the trip-summary surface.
- **Corpora are mock** (harvested from ino's `MockFlightCorpus` / `MockHotelCorpus` /
  `MockEventsCorpus` / `MockActivityCorpus` / `MockWeatherCorpus`). No real travel APIs.

**RFW emission:** ino's `RfwPayload(LibraryName, DescriptionDsl, DataPayload)` is mapped to
brain's `UiSurface.ForRfw(libraryName, rootWidget, dataJson, source)`. The card libraries
(`ino.travel.flights`, `.hotels`, etc.) and the `onSelect: event '<name>' { args }` DSL
syntax port over; the Flutter side already renders RFW surfaces.

**Tap carrier:** today `ExperienceUsed` does not carry an arbitrary args map. The slice
introduces a small typed step synapse (analogous to ino's `RfwEventRequest`):

```csharp
[GenerateSerializer]
public record ExperienceStep(string Pack, string ExperienceId, string EventName,
    IReadOnlyDictionary<string, string> Args) : Synapse(nameof(ExperienceStep), DateTimeOffset.UtcNow);
```

The travel pack's `GetManifest()` declares it handles `ExperienceStep` (+ `ExperienceUsed`
for entry); `CanHandle` gates on `Pack`/`ExperienceId`.

### 3.3 Data flow (one hop)

```
Flutter tap on a card
  → UiInputSynapse / EngageUiSession (existing bidirectional gRPC)
  → gateway Send → ExperienceStep synapse
  → GeneratedNeuron[generated-travel].Handle → travel IPackBehavior (stateful)
  → returns next UiSurface.ForRfw(...)
  → HomeFeedBus fanout → WatchHomeFeed (gRPC-Web)
  → Flutter RFW host renders next card (semantics identifier = surfaceId)
```

The entry hop is the same minus the tap (triggered by `ExperienceUsed`).

### 3.4 E2E harness (harvested + adapted)

Add to `DigitalBrain.Tests/E2E/`:

- A small `ExperienceFlowDriver` (harvested ergonomics from ino's `NeuronE2ETest`):
  helpers `TriggerExperienceAsync(pack, experienceId)` and
  `TapAsync(pack, experienceId, eventName, args)` built on the **existing**
  `DigitalBrainAppHostFixture` (which already boots the AppHost, waits for `kernel`
  health, and exposes `PublishPackAsync`/`InstallPackAsync`/`SendSynapseAsync`). We are
  NOT porting ino's `InoBrowserFixture`/`NeuronAppHostFixture` wholesale — brain already
  has the equivalent fixture; we add the per-hop driver on top.

- One new gated E2E test `TravelPlanTripRendersE2ETests` mirroring ino's
  `RichTripPlanningE2ETests`, but asserting through Flutter (Playwright), not just gRPC:
  1. Publish + install the travel pack.
  2. Trigger "Plan a trip"; assert the intro surface renders
     (`[flt-semantics-identifier="travel-intro"]`).
  3. Tap `flight.selected`; assert the hotels surface renders.
  4. Continue through hotel → event → activity → summary, asserting each hop's surface.
  - Gated behind the existing `RUN_FLUTTER_E2E` + prebuilt web bundle, tagged
    `[Trait("Category","E2E")]`, single-replica (`DIGITALBRAIN_KERNEL_REPLICAS=1`) for
    deterministic fanout — same conventions as `PackEmbodimentRendersE2ETests`.

## 4. Phasing

- **Phase 1 — Travel slice (this spec).** Build everything above using current
  `NeuroPack` naming. Introduce the additive `Experience` + `ExperienceStep` types. Prove
  the up-to-Flutter E2E.
- **Phase 2 — Rename sweep (follow-up spec).** `NeuroPack` → `Domain` across
  Core/Kernel/Aspire/Mcp/Tests/app; rename `domain_palette.dart` → `cluster_palette.dart`;
  audit remaining `pack` references. Mechanical, done once the model is proven.

## 5. Explicitly NOT doing (YAGNI)

- No silo-per-domain (ino's hosting model). brain stays pack/ALC based.
- No real travel APIs — mock corpora only.
- No per-user flow state — per-install grain state is sufficient for the slice.
- No cross-origin (prod GitHub Pages ↔ remote kernel) E2E.
- No marketplace auth/commission/security-enforcement work.
- No rename in Phase 1.

## 6. Testing & verification

- **Inner loop:** `dotnet build` + targeted `dotnet test` for the travel pack behavior
  (pure unit tests over the hops with mock corpora — fast, no cluster).
- **E2E:** the gated `TravelPlanTripRendersE2ETests` (opt-in via `RUN_FLUTTER_E2E`).
- **Ritual:** `dotnet build`, relevant `dotnet test`, `aspire doctor` after changes
  (per `brain/AGENTS.md`).
- **Context7** for any Aspire.Hosting.Testing / Playwright / Orleans / RFW API used.

## 7. Open risks to confirm during planning

1. The exact tap path from Flutter (`UiInputSynapse`/`EngageUiSession`) to a pack-handled
   synapse — confirm whether a card's `onSelect` can target `ExperienceStep` directly or
   needs a small mapping in the UI gateway.
2. Whether `UiSurface.ForRfw` carries the per-hop `surfaceId` through `HomeFeedBus` →
   `RfwCardEnvelope.CorrelationId` so each hop gets a distinct, assertable semantics id
   (the existing E2E threads one id; we need one per hop).
3. RFW card libraries (`ino.travel.*`) must be registered/renderable on brain's Flutter
   side; confirm the RFW host resolves them or port the widget definitions.
