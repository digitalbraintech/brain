# Cleanup, Refactoring & Simplification: 30 Next Steps Plan (Neuron/Synapse Purity + Proof)

**Date:** 2026-07-01  
**Status:** Actionable 30-item plan (prioritized P0-P3)  
**Goal:** Achieve **proof** (via tests, E2E, aspire flows, manual demo) that the system works *exactly* as envisioned:  
- **Everything** (logic, UI, channels, viz, comms, marketplace, dev hosting) is **neurons + synapses**. No special snowflakes.  
- **Flutter "neuron"** (or UiPresentationNeuron) handles UiSurface / widget-tree synapses.  
- **Telegram** (self-contained via ITelegramChatNeuron + channel synapses) can trigger UI output (e.g. "visualize this Excel chart data" from Telegram message → UiSurface with data viz).  
- **Dev default**: `aspire run` (or `brain.cs`) locally always starts the Windows Flutter client (even while UI remains a first-class marketplace "ino"/NeuroPack bundle).  
- Clean abstractions: `ITelegram*`, `IFlutterUi*`, `IChannel*` patterns expressed purely as `INeuron` + `IHandle<TSynapse>` (or embodied packs).  
- Massive cleanup/refactor/simplification (delete > add) following Elon's algorithm.  
- All changes keep Aspire integration tests green, use latest packages, Context7 lookups, aspire MCP tools, relative paths, targeted high-severity tests + ritual after edits.

This extends the prior root plan (`2026-07-01-root-neuron-synapse-telegram-ui-simplification-plan.md`). Focus on ~30 concrete, tiny-scope, verifiable items.

## Vision Alignment (Root Law Reinforced)

- **Synapses** = the universal metadata + datatypes (UiSurface, TelegramReplyRequested via Signal or typed, VisualizeDataRequest, DataChartGenerated, etc.). They carry correlation/causation for context (e.g. "this came from Telegram → prefer Telegram or route to UI").
- **Neurons** = logic carriers (grains or `IPackBehavior` packs). Examples: `TelegramChatNeuron`, `ChartNeuron`/`DataVisualizationNeuron`, new `FlutterUiNeuron` or `UiPresentationNeuron`, `MarketplaceNeuron`, packs emitting surfaces.
- **Kernel** = the pure runtime substrate (Orleans + embodiment + dispatch + journals + timeline broadcast for N+1).
- **Adapters** (thin only): Telegram.Transport (webhook/BotAPI), Flutter client (RFW/ForUI thin host + gRPC for UiSurface), Aspire/AppHost (orchestrates kernel replicas + default clients), Gateway/MCP (entry). Adapters translate external ↔ synapses; **no logic**.
- **Marketplace bundles** ("ino" = NeuroPack + BundleManifest): UI (DigitalBrain.UI.* including AspireFlutter + ForUI kit), Telegram (DigitalBrain.Telegram.Responder + channel logic). Installable, configurable, upgradable at runtime.
- **Cross-communication proof example**: Telegram inbound Signal → (bound or broadcast) → neuron logic (or pack) that reads/processes "Excel" data (via Sdk neurons or request) → calls chart viz → emits `UiSurface` (or `DataChart*`) → Flutter "neuron"/client renders interactive chart. All via synapses, no direct coupling.
- **Dev ergonomics**: UI bundle lives in marketplace (installable, upgradable via pack), but local `aspire run` / `brain.cs` defaults to starting the Windows Flutter executable for fast feedback (thin host only; surfaces come from kernel neurons).

Current trash (identified root-out from Core → Kernel → Aspire → seeds → tests):
- Conditional/arg-based Flutter start vs. unconditional in AppHost (inconsistent defaults).
- No dedicated "Flutter neuron" handling Ui* synapses (surfaces emitted ad-hoc from SystemNeurons, ChatNeuron, ChartNeuron; bridged but no single IHandle<UiSurface> owner for the channel).
- Duplication (seeds strings vs. real classes for Telegram/UI behaviors; string literals for signal names).
- Leaky special cases (Telegram split, UI start logic duplicated in AppHost/brain.cs, some UI surfaces still using old RfwCard paths).
- Weak cross-channel proof (no E2E showing Telegram → UI viz flow; Excel/data viz exists in ChartNeuron but not wired from channel input).
- Missing clean abstractions for channels (ITelegramChatNeuron exists; no parallel IFlutterUiNeuron or general IUiPresentationNeuron).
- Hosting "magic" (path resolution, explicit --flutter, env gates) instead of pure neuron-driven or pack-provided.
- Tests not proving the full desired model (many unit but limited aspire-integrated cross-neuron flows).
- Dead/legacy (old Ino refs, unused gateways in default paths, scattered consts).
- Over-abstraction in places + under in channels.

## Elon Musk 5-Step Approach (Applied Strictly)

**1. Make requirements less dumb**  
Questioned: "Must UI always be optional pack even in dev?" → No, default thin client for local aspire while keeping marketplace model. "Need new IFlutter interface with methods?" → Prefer synapse-driven (IHandle<UiSurface> on a dedicated neuron) like Telegram. "Does every cross-comm need new grain?" → No, reuse/extend existing (Chart + new thin router neuron) + causation. Trace every item to "pure neurons/synapses + proof + dev default + Telegram→UI example".

**2. Delete** (target 30-50% reduction in special cases)  
- Delete duplicated start logic / path resolvers (unify or move to pack-provided Aspire bits).  
- Delete string literals (use `TelegramSignals`, `UiSurfaceKinds`).  
- Delete ad-hoc surface emission (route via dedicated Flutter/Ui neuron).  
- Delete conditional Flutter in canonical AppHost (make default for local).  
- Delete separate "ino" terminology in favor of consistent "NeuroPack / bundle".  
- Prune legacy RfwCard paths where UiSurface suffices.  
- Remove explicit --flutter requirement for default local runs.

**3. Simplify**  
- One pattern for channels: `ITelegramChatNeuron : INeuron, IHandle<Signal>`, new `IFlutterUiNeuron : INeuron, IHandle<UiSurface>` (or `IUiPresentationNeuron`).  
- Synapses as the comms abstraction (Telegram input produces UiSurface output via shared neurons; use CorrelationId/CausationId for "reply context" or "origin channel").  
- UI bundle + client: marketplace pack for logic/surfaces + thin dev default executable (inspired by "UI is also ino bundle").  
- Data flow: Telegram Signal → (neuron/pack) → Excel/data request (Sdk) → Chart/Visualize → UiSurface emission → Flutter neuron/client.  
- Tests: one harness proves neuron → synapse → cross-channel → surface.

**4. Accelerate cycle time**  
- Default Windows client on `aspire run` (fast visual loop).  
- Targeted filters + BundleHarness/PackAlcEmbodier for pack/UI tests.  
- Warm cluster + e2e.runsettings already exist; extend for channel flows.  
- Use aspire MCP (`list_resources`, `execute_resource_command` for flutter-ui restart, `doctor`) instead of full restarts.

**5. Automate last**  
After proof: auto-install of default UI/Telegram seeds on fresh marketplace, closed-loop authoring that emits cross-channel examples, CI that runs full aspire + Telegram-sim + Flutter-render proof.

## Clear Priorities

- **P0 (hours-days, proof core)**: Defaults, abstractions (IFlutter etc.), Telegram→UI synapse flow, basic tests. Must have working demo that "Telegram asks for viz → UiSurface arrives in Flutter".
- **P1 (1 week)**: Major cleanup/delete, dedicated neurons, unified hosting.
- **P2**: Full E2E aspire integration tests for the flows, bundle tests per channel.
- **P3**: Polish, docs, further deletes, automation.

**Verification ritual (mandatory after every item/slice with code impact)**:  
`dotnet build` (relative) → relevant `dotnet test --filter "..."` (high severity, aspire.dev/E2E green) → `aspire__doctor` + targeted MCP (`list_resources`, restart flutter-ui if touched) → update this plan with outcome. Use Context7 (`context7__resolve-library-id` + query) for **every** Orleans/Aspire/gRPC/ serialization API before edit. Latest packages via Directory.Packages.props. No C:\Users paths. Self-explanatory names only.

Use aspire MCP tools for all hosting changes.

## ~30 Next Steps (Prioritized, Tiny Scope, Verifiable)

1. **P0** Unify + default Windows Flutter client in AppHost.cs (remove any remaining path/env conditionals for local dev; always resolve + start "windows" as default thin host while UI remains marketplace pack). Proof: `aspire run` starts flutter-ui without --flutter or extra flags.
2. **P0** Make brain.cs default to withFlutter=true for local dev (or document as the "packed" path still requires arg but AppHost is canonical for aspire).
3. **P0** Add `IFlutterUiNeuron : INeuron, IHandle<UiSurface>, IHandle<UiWidgetTree>` (and related) in DigitalBrain.Core/Synapse.cs (next to ITelegramChatNeuron).
4. **P0** Implement `FlutterUiNeuron` (or `UiPresentationNeuron`) in DigitalBrain.Kernel/Ui/ that IHandle<UiSurface> (forwards to HomeFeedBus / UiGateway or manages presentation state). Make it subscribe appropriately.
5. **P0** Update ChatNeuron, ChartNeuron, SystemNeurons (UI emissions) to also deliver relevant Ui* to the new FlutterUiNeuron grain (point-to-point or via typed synapse) for "handling".
6. **P0** Extend TelegramChatNeuron (or add handler) so Telegram inbound can produce UiSurface (route "viz" commands to ChartNeuron which emits UiSurface).
7. **P0** Add minimal cross-channel test: Telegram Signal (with "chart excel sales") → bound/generated or broadcast → Chart/ viz logic → UiSurface emitted → assert FlutterUiNeuron (or bus) received it. Use TestCluster.
8. **P0** Simulate "excel file" input: extend VisualizeDataRequest or add simple DataRequest synapse; have a test neuron/pack "load" json-as-excel and emit chart UiSurface. Proof: end-to-end synapse chain.
9. **P0** Update MarketplaceSeeds UI packs (DigitalBrain.UI.*) and Telegram to ensure BundleManifest + auto visibility (tie to prior auto-publish work).
10. **P0** Add `TelegramSignals` / `UiSignals` consts (or expand in Core/Signals.cs) and replace remaining literals (cleanup from prior).
11. **P1** Centralize all channel signal names + Ui kinds in Core (delete dupe in Kernel/Telegram/ tests).
12. **P1** Refactor AppHost.cs + brain.cs + DigitalBrainBuilderExtensions: extract Flutter start into a "dev default" helper; document that the "DigitalBrain.UI.AspireFlutter" pack can provide/override the resource bits.
13. **P1** Create thin `IChannelNeuron` or use existing pattern; make TelegramChatNeuron and FlutterUiNeuron implement a common marker + share reply/context logic via CorrelationId.
14. **P1** Update UiSurfaceRfwBridge + HomeFeed to prefer routing through the new FlutterUiNeuron when present.
15. **P1** Add DataVisualization / chart example that accepts "from telegram" context (use causation or props) and prefers emitting UiSurface (already does via samples).
16. **P1** Ensure `DataVisualizationNeuron` / `ChartNeuron` implements or is discoverable as handling Ui output; wire a "ExcelVizPack" seed example that a Telegram responder can trigger.
17. **P1** Add Reqnroll or xUnit E2E slice (under DigitalBrain.Tests/E2E or Telegram/) that uses aspire hosting test + simulated Telegram input → UI surface assertion (via gateway or bus spy).
18. **P1** Update starter authoring bundles + tests to demonstrate cross-channel (Telegram trigger → UI surface emission).
19. **P1** Delete duplicated Flutter path resolution logic (share between AppHost/brain.cs or move to a small helper in Aspire project).
20. **P2** Audit + delete legacy RfwCard paths in favor of pure UiSurface (in SystemNeurons, bridges, tests).
21. **P2** Make UI client "pack aware": when "DigitalBrain.UI.AspireFlutter" (or equivalent) is installed from marketplace, the Aspire resource is still provided but surfaces come from the embodied pack (already partial intent).
22. **P2** Introduce general `ChannelReply` or keep typed per-channel but route via a thin `ChannelRouterNeuron` (simplifies "reply in originating channel").
23. **P2** Add proof test: full publish/install of Telegram bundle + UI bundle (seeds) → fire Telegram message → receive UiSurface in "Flutter" path.
24. **P2** Cleanup MarketplaceSeeds: remove any hard-coded surface fakes now that auto-publish + manifests work; rely on real neuron emissions.
25. **P2** Refactor Kernel/Program.cs + startup to explicitly activate FlutterUiNeuron (like LlmResponderNeuron singleton activation) for broadcast reach.
26. **P2** Use Context7 (resolve + query) for latest Orleans  (v10/ grains, journaling) + Aspire 13+ hosting (AddExecutable, WithReference) before any further hosting refactors. Update any outdated patterns.
27. **P3** Add per-bundle runnable tests (TelegramBundleTests, UIBundleTests) that embody the seed, fire channel synapse, assert cross UiSurface.
28. **P3** Delete explicit IsEnabled gates where possible or make Telegram + default Flutter the "core channels" always present (simplification).
29. **P3** Update SYSTEM_DESIGN.md, authoring-a-bundle.md, CONTINUITY with the new neuron abstractions (IFlutterUiNeuron etc.) and the Telegram→UI viz proof example.
30. **P3** Final proof ritual: full `aspire__doctor`, build, high-sev test filter including E2E/aspire, manual `aspire run` (confirm windows client + Telegram sim if token) showing the exact desired flow. Tag as "system works as wanted".

**Bonus P3 items (if capacity)**:  
31. Generalize Sdk neurons (IFileSystemNeuron etc.) so "excel file" can be real in a pack triggered from Telegram.  
32. Make the Windows client target configurable via env but default "windows" for local dev machines.  
33. Add golden or contract test for UiSurface produced from channel input.  
34. Review all `Special` neurons (AspireOrchestrator etc.) for further push to pure synapse emission.

## Execution Notes

- **Order**: P0 first (proof + default + abstraction). Each item is a small PR/slice.
- **Proof criteria**: 
  - A test or aspire-integrated scenario: Telegram message → neuron → UiSurface (chart viz) → "handled" by FlutterUiNeuron / reaches client.
  - `aspire run` starts flutter-ui by default (Windows).
  - UI and Telegram are marketplace bundles (installable, with manifests).
  - All via INeuron + synapses (no direct calls between channels).
- **Tools**: Always `aspire__*` MCP for resources. Context7 before Orleans/Aspire edits. `dotnet test` targeted. Delete-first.
- **Risk / blast**: Every item scoped to 1-3 files + test. Reversible.
- **After this plan**: The system *proves* the pure neuron/synapse model with the exact user scenarios (Telegram comm → UI viz, dev client default, bundles in marketplace, clean channel abstractions).

Start with items 1-8 (P0 core proof). Update this file with results after each.

This is the root-out, delete-heavy, Musk-ordered path to the exact desired system.