# Root-Out Simplification Plan: Everything Is Neurons + Synapses (Telegram + UI as Exemplars)

**Date:** 2026-07-01  
**Status:** Plan for tiniest viable slices leading to working demo in hours  
**Approach:** Elon's 5-step algorithm (in strict order) + root-out from Core protocol.  
**Goal:** Telegram bundle (with config + rich reply) and UI bundles are first-class, auto-visible, installable from marketplace out-of-box. Introduce minimal self-contained reply for channel context. Delete trash. Make "all the system" visibly just neurons firing/handling synapses. No new big subsystems.

## The Root Vision (Core Law, reinforced)

- **Synapses** = metadata + immutable datatypes (the "what happened" or "do this" carriers, with Stamp lineage for causation/correlation).
- **Neurons** = carriers of logic (Orleans grains implementing `IHandle<TSynapse>` or embodied `IPackBehavior`).
- Kernel project = the runtime substrate that hosts/activates all neurons, provides journals, timeline broadcast (N+1 via GeneratedNeuron + PackAlcEmbodier), dispatch.
- **Everything else** = expressed as neurons + synapses (or thin adapters that translate external events <-> synapses).
  - Marketplace = `MarketplaceNeuron`.
  - LLM calls = `LlmResponderNeuron` + `AskLlm` / `Signal` indirection (packs stay pure).
  - Telegram channel = `TelegramChatNeuron` (self-contained for routing + replies) + embodied responder pack(s).
  - UI (including dynamic components) = `UiSurface` (a Synapse) + `UiWidgetTree` emissions from any neuron/pack. "DigitalBrain.UI.*" = nice authoring types in Core for packs to construct surfaces.
  - Hosting (Aspire, AppHost, transport processes) = orchestration of kernel replicas + channel adapters. They are **not** neurons; they feed and observe synapses.
- No logic leaks into adapters. No special snowflakes. New capabilities ship as NeuroPacks (bundles) via marketplace.

Current "trash" (identified root-out):
- Seeds exist but are not auto-journaled → marketplace does not show Telegram/UI bundles reliably out-of-box (MCP tools paper over it; surfaces sometimes fake with seeds).
- Telegram seeds lack `GetBundleManifest()` → not faceted as proper `Channel.Telegram`.
- Reply path is stringly-typed `Signal("TelegramReplyRequested")` only (no typed `TelegramReply` that any neuron can fire to target the channel).
- Inbound context (telegram origin) is not easily usable for "reply in same channel" without manual chatId plumbing in every pack.
- Duplication: `DigitalBrain.Telegram/Synapses.cs` + `TelegramResponderNeuron.cs` (real types) vs. identical source strings in `MarketplaceSeeds`.
- UI authoring inside packs is good via `KitExperience` but lacks ultra-light `DigitalBrain.UI.Label(...)` style helpers for any neuron (not just full experiences).
- Scattered string literals for channel names.
- Special projects (Telegram, Aspire, Gateway, Mcp) have grown some non-neuron logic that can be pushed down or clarified as "adapter only".
- Legacy comments / .ino references / demo surface code that no longer match the pure neuron model.

## Elon's 5 Steps Applied (this plan)

### 1. Make requirements less dumb
- Do we need a full new "ITelegram" interface or separate TelegramNeuron grain right now? No — extend `TelegramChatNeuron` (already the per-chat router) + a public typed synapse. Keep transport as the minimal adapter.
- Do we need complete UI component library in one slice? No — tiny static helpers + ensure existing UI seeds are published + one demo emission.
- Do we need to delete entire projects today? No. Clarify boundaries and push logic into neurons where it belongs.
- "Telegram must be self-contained with Reply" and "UI shippable like Telegram" are the two exemplars that prove the root model.
- Traceability: All changes must make a fresh `dotnet test --filter "Telegram|Marketplace|UiSurface"` + seed install path demonstrably better.

### 2. Delete (ruthlessly)
- Delete reliance on manual MCP publish for first-party bundles (auto-ensure in the neuron).
- Delete (or const-ify + centralize) magic strings for Telegram signals.
- Delete duplication where the seed string is the publish truth; keep real .cs only for compile-time test reference (or delete the class if tests can live on the string alone).
- Delete any UI construction that bypasses `UiSurface` / neuron emission.
- In this plan: no new grains, no new assemblies for "UI lib", no big manifest changes.

### 3. Simplify what remains
- One new tiny public type pair in Core: `TelegramReply` + `TelegramButton` (or reuse props on existing patterns).
- `TelegramChatNeuron` becomes the self-contained "Telegram reply router" by also handling the new reply synapse (converts to the Signal the transport already watches).
- Any neuron/pack can now `Fire(new TelegramReply(...))` or return it from `Handle` and the system routes to Telegram (demo of "send it to telegram").
- For inbound context: the `Signal("TelegramMessageReceived")` already carries `chatId`. Packs that want channel-aware reply just emit `TelegramReply` using that value (or we can later stamp a correlation-based default router).
- UI: Add 4-6 tiny creators under `DigitalBrain.Core` (or `UiSurfaces` extensions) so pack code reads as `UI.Column(UI.Label("foo"), UI.Button("Send", "send"))`. They produce the existing `UiWidgetTree`. No new render path.
- Auto-publish + proper manifests = "in marketplace out of box" with almost zero new code.

### 4. Accelerate cycle time
- All changes testable with `dotnet test --filter "Telegram|Marketplace|Distribution|Ui" ` (in-proc TestCluster + PackAlcEmbodier, no Aspire required for core demo).
- Use `BundleHarness` / direct embodiment for UI behavior packs.
- After each micro-slice: build + targeted test + `aspire doctor` (MCP).
- Tiniest slices first so a working "install Telegram from marketplace, receive, reply with button via typed synapse" is live in < 2 hours.

### 5. Automate last
- After demo is solid: consider auto-seed on grain activate, a one-command "publish all first-party", or making the authoring closed-loop emit the TelegramReply automatically for channel context. Not now.

## Tiniest Scope Steps (Root-Out Order, Executable in Hours)

Do in this sequence. Each is reversible, small blast radius, produces visible progress toward "working demo".

### Step 0 — Baseline (5 min)
- Run (relative):
  ```
  cd brain
  dotnet build
  dotnet test --filter "FullyQualifiedName~Telegram|Marketplace|BundleManifest" --no-build
  ```
- Use aspire MCP: `aspire__doctor`.
- Confirm current state: Telegram seeds in `MarketplaceSeeds` but not auto in journaled `PublishedList`; no `BundleManifest` on them; no `TelegramReply` type.

### Step 1 — Auto-publish first-party seeds (marketplace "out of box") — tiniest high-impact
- Root: `MarketplaceNeuron` (in `DigitalBrain.Kernel/SystemNeurons.cs`) is the single source of truth for published packs (cache from journal only).
- Change: In `EnsureCache` (or a new `EnsureFirstPartySeeds()` called from `GetPublishedPacks` / `ListPublished` / `FilterMarketplace`), after replaying journal, for any seed in `MarketplaceSeeds.LocalUiPacks` that has Code and is missing, fire the already-signed `ToPublishCommand(seed)`.
- Idempotent: only fire if `!_publishedCache.ContainsKey(...)`.
- This makes `ListPublished`, install, and channel filters see "DigitalBrain.Telegram.Responder", "DigitalBrain.Telegram.KeywordWatcher", and the UI.* packs immediately.
- Also update the places that currently overlay seeds only for UI surfaces (UserSessionNeuron etc.) to be consistent or rely on the real list.
- Test delta: Add fact that after `ListPublished` the Telegram packs are present without prior explicit publish.
- Files touched: `SystemNeurons.cs` (tiny), one test file.
- Demo win: `market` neuron now surfaces them; `InstallFromMarketplace` for Telegram works from clean start.

### Step 2 — Telegram bundles declare proper Channel manifest (faceting + "bundle" reality)
- Root: `BundleManifest` already exists in Core. `KitExperience` shows the pattern for Content/InApp. `MaterializeManifest` embodies to read it.
- Change: In the `TelegramResponderPackCode` const (and KeywordWatcher) inside `MarketplaceSeeds.cs`, add/override:
  ```csharp
  public BundleManifest? GetBundleManifest() => new(
      BundleTier.Channel,
      null,
      new[] { BundleChannel.Telegram });
  ```
- Mirror the same override in the reference `DigitalBrain.Telegram/TelegramResponderNeuron.cs` (for test parity).
- Update `ResponderPackTests.cs` and `BundleManifestEmbodimentTests.cs` (or MarketplaceFacetTests) to assert the Channel manifest after embodiment.
- Files: `MarketplaceSeeds.cs`, `TelegramResponderNeuron.cs`, 1-2 test files.
- Demo win: `FilterMarketplace(Channel=Telegram)` now shows the bundles. Marketplace UI can surface them under "Channels".

### Step 3 — Introduce typed Reply synapse for self-contained Telegram (channel context + "send it to telegram")
- Root: Synapses are the metadata/datatypes. Any neuron must be able to emit a reply that the Telegram channel understands without knowing transport details.
- Add to Core (new or extend `Signals.cs` / a minimal `Channels/Telegram.cs` — keep tiny, one file):
  ```csharp
  [GenerateSerializer]
  public record TelegramButton(string Text, string? CallbackData = null, string? Url = null);

  [GenerateSerializer]
  public record TelegramReply(
      [property: Id(0)] long ChatId,
      [property: Id(1)] string Text,
      [property: Id(2)] IReadOnlyList<IReadOnlyList<TelegramButton>>? InlineKeyboard = null)
      : Synapse(nameof(TelegramReply), DateTimeOffset.UtcNow);
  ```
- Make `TelegramChatNeuron` also `IHandle<TelegramReply>` (and keep `IHandle<Signal>`).
  In the handler:
  - Build props dict from the `TelegramReply` (include "buttons" as serializable structure if present).
  - `await Broadcast(new Signal("TelegramReplyRequested", props));`  (or the exact shape transport expects).
  - This keeps transport unchanged for the demo.
- Update `TelegramChatNeuronTests.cs` with a case: deliver a `TelegramReply` → assert the corresponding Signal is emitted on outgoing.
- Tiny demo in one seed (e.g. enhance `KeywordWatcherPackCode` or add a one-off in tests): when it sees a trigger, return a `TelegramReply` with 1-2 buttons in rows (position via nested lists).
- Update `DigitalBrain.Telegram/Synapses.cs` comment to clarify transport-internal records are adapters only; public API for packs/neurons is the new `TelegramReply` (or Signal + the typed one).
- For "understands user sent via telegram": inbound path (in `TelegramChatNeuron.HandleAsync` for `TelegramMessageReceived`) can optionally attach extra props or we rely on the caller (the bound generated neuron or a smart pack) having the chatId from the incoming Signal. For the demo this is sufficient and explicit ("send it to telegram" = construct `TelegramReply` with the id).
- Later (not this demo): a thin `ReplyRouterNeuron` that turns generic `Reply` into channel-specific using stored context/correlation.
- Files: Core (small new records), `TelegramChatNeuron.cs`, its test, one seed string, transport comment.
- Demo win: From a test, any neuron, or an embodied pack: `Fire(new TelegramReply(chatId, "Hello with options", new[] { new[] { new TelegramButton("Yes", "yes"), new TelegramButton("No", "no") } }));` → message with positioned buttons arrives in Telegram.

### Step 4 — Make UI shippable + ergonomic inside neurons/packs (symmetric to Telegram)
- Root: `UiSurface` is already a Synapse. `UiWidgetTree` + `ForWidgetTree` / `ForExperienceHopTree` already let neurons author UI. Seeded packs (`DigitalBrain.UIKit.ForUI`, `DigitalBrain.UI.Workbench`, gallery etc.) already exist.
- Ensure Step 1 makes the UI.* packs appear in marketplace.
- Add minimal ergonomic helpers (delete the "stringly" pain for common cases). In `DigitalBrain.Core` (or extend `UiSurfaces.cs` / new tiny `Ui.cs`):
  - `public static class Ui { public static UiWidgetTree Label(string t) => new("label", new(){["text"]=t}); public static UiWidgetTree Button(string label, string eventName) => ...; public static UiWidgetTree Column(params UiWidgetTree[] kids) => new("column", ..., kids); ... }`
  - Or static methods that return the nodes used by the existing renderer.
- Update one simple behavior pack seed (or the Dummy) or a test pack to emit a `UiSurface.ForWidgetTree( Ui.Column( Ui.Label("From neuron"), Ui.Button("Reply via Telegram", "tg-reply") ) )`.
- Assert in a Ui test that the tree round-trips and contains the nodes.
- This lets pack authors write `Fire( new UiSurface.ForWidgetTree( DigitalBrain.UI.Column(...) ) );` (or whatever naming we choose) — matching the user's mental model.
- Files: Core (tiny helpers), one seed or new minimal example, a contract/UI test.
- Demo win: Install a UI bundle from marketplace (now out-of-box) + see or trigger a surface built with the nice API. Existing ForUI + RFW continue to work.

### Step 5 — Delete trash + const centralization (ongoing in every slice)
- Centralize the two Telegram signal names as `public const string` in Core (e.g. `TelegramSignals.MessageReceived`, `ReplyRequested`).
- Replace all literals in `TelegramChatNeuron`, responder code (string + real class), transport dispatcher, tests, docs references.
- In `MarketplaceSeeds.cs` and responder, remove redundant comments that duplicate the model.
- Delete or mark as "adapter only" the transport-internal record types if they are no longer the public story.
- While touching seeds: ensure `Telegram*` pack descriptions clearly say "Channel bundle — install to enable rich Telegram I/O via `TelegramReply` synapse".
- Any other obvious dead paths found during the slices (e.g. old demo surfaces) get a delete pass.
- Root effect: the only "special" things left are the thin adapters that must exist (HTTP ingress for Telegram, Aspire resource graph, gRPC gateway for clients).

### Step 6 — Working demo verification + runnable bundle tests (closure for this session)
- One focused test (or extend `ResponderPackTests` + `TelegramChatNeuronTests` + a Marketplace flow test):
  1. Auto-seed ensures packs listed.
  2. Install `DigitalBrain.Telegram.Responder` (or Keyword) via marketplace neuron.
  3. Provide minimal config (token scope) via test store if needed for full path.
  4. Deliver inbound `Signal("TelegramMessageReceived")` (or via chat neuron).
  5. Assert pack produces `AskLlm` or direct reply.
  6. Fire a `TelegramReply` with buttons from the test (or from the embodied pack).
  7. Assert the resulting `Signal("TelegramReplyRequested")` with button data appears (transport contract can stay at signal level).
- For UI: similar tiny flow emitting a widget tree via the new helpers from a behavior pack.
- Run: targeted tests, `aspire__doctor`, build.
- If time: attach to warm kernel or use `aspire__execute_resource_command` for a flutter-ui restart and spot-check surfaces (optional for the core demo).
- Update `SYSTEM_DESIGN.md` (or a short note) and the authoring bundle doc to call out the new `TelegramReply` + UI helpers as the pattern for channel/UI capabilities.
- Success criteria for "working in a couple of hours":
  - `dotnet test --filter "...Telegram..."` green with new reply path.
  - Fresh cluster `ListPublished` contains the Telegram bundles without prior manual publish.
  - `FilterMarketplace` by Telegram channel returns them.
  - Code can `Fire(new TelegramReply(...))` and it routes.
  - A pack can emit nice `Ui.*` constructed surface.
  - No new hosting concepts; everything routed through neurons/synapses.

## Additional Optimizations / Simplifications Considered (for after demo or parallel tiny slices)

- Merge the two Telegram seeds into one richer "DigitalBrain.Telegram" Channel bundle that both handles inbound LLM and provides the reply capability (delete one pack name).
- Make `Signal` even more universal; introduce a generic `Reply` base + channel-specific only when needed (further delete).
- Push the `TelegramChatNeuron` binding logic into an embodied pack if it can stay pure (journal scan may be hard; keep grain for now — it's already a neuron).
- For UI: once helpers land, evolve the "DigitalBrain.UIKit.ForUI" seed to also publish the C# helper types (or keep them in Core as the universal kit — packs compile against Core).
- Full root cleanup: audit every non-neuron class and ask "can its logic live in a neuron that emits/handles synapses?" Most already can.
- Later: a single `ChannelReply` synapse + pluggable routers (Telegram, future Web, etc.) instead of per-channel reply records.
- Test isolation: each bundle seed gets its own `*BundleTests.cs` that only references the const string + harness. Makes adding new "ino" (C# pack) trivial and parallel.
- Delete more: if the legacy diagnostic gateway is off by default and unused in happy path, mark for removal in a future slice.

## Execution Rules (non-negotiable)

- Relative paths only. Use `brain/...`.
- Before touching any API (Orleans, gRPC, Telegram.BotAPI, Aspire resources, config store, etc.): use context7 MCP lookup for current signatures.
- After every slice (even docs-only that affects build): `dotnet build`, relevant `dotnet test --filter`, `aspire__doctor` (and other aspire MCP tools for resources).
- Prefer delete. Small focused commits.
- Use `BundleHarness` / `PackAlcEmbodier` + TestCluster for fast feedback.
- When the demo slice is done: run full relevant high-severity tests and confirm aspire integration paths are green before claiming victory.
- Update this plan file with actual outcomes / deviations as slices land.

## Execution Log (this session)

- 2026-07-01: Plan written.
- Micro-slice executed: Step 2 (proper BundleManifest for Telegram channel bundles).
  - Added `GetBundleManifest()` returning `BundleTier.Channel, [BundleChannel.Telegram]` to:
    - `TelegramResponderPackCode` (the publish truth in MarketplaceSeeds.cs).
    - Reference `TelegramResponderNeuron.cs`.
    - `KeywordWatcherPackCode`.
  - Build: 0 errors.
  - Relevant tests (`~Telegram|Marketplace|BundleManifest|ResponderPack`): 39+ passed.
  - `aspire__doctor`: clean (4/4).
- Observation (more optimization): `Ui` static class + `NeuronUiKit` + `UiWidgetTree` + `UiExperience` fluent already exist in `DigitalBrain.Core/UiSurfaces.cs` and `UiKit/`. This is exactly the "dynamic ui library components" (`Ui.Label`, `Ui.Button`, `Ui.Column` etc.). No new API surface needed for the UI-shippable demo — just ensure the UI.* seeds are auto-published (Step 1) and document usage from any `IPackBehavior.Handle` via `UiSurface.ForWidgetTree(Ui.Column(...))`. This is further "delete" and "simplify".
- Further root-out idea: The typed `TelegramReply` (Step 3) can be minimal; alternatively a `TelegramReplies` static helper returning `Signal` reuses the universal carrier and deletes a record type for v1 demo.

## Success & Next After Quick Demo

A fresh run shows Telegram and UI bundles in the marketplace (via auto-publish + manifests), you can install the Telegram one, send a message, and from any neuron fire channel-aware replies (typed or via convention). UI surfaces authored with the existing clean `Ui.*` component style inside packs and emitted as `UiSurface` synapses. The mental model "kernel hosts neurons; the rest of the system is neurons firing synapses — all of them" is visibly true.

Next (post-demo, still tiny slices): implement auto-publish (Step 1), add minimal `TelegramReply` routing (Step 3), centralize the two Telegram signal names as consts in Core/Signals.cs (delete string trash), make one pack demo both a TelegramReply emission and a Ui.* surface. Run full ritual each time.

This plan is the root-out, delete-first, tiniest-step path to the vision. Start at Step 1.
