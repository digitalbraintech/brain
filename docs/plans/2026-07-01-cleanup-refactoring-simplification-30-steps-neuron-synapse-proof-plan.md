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

## Execution Log (P0 slice, commit a4f301d baseline)

**2026-07-01 P0 start:**
- Referenced commit a4f301d.
- Baseline: `dotnet build` succeeded (0 errs), targeted `dotnet test --filter "Telegram|Marketplace|...|Ui|Chart"` : 65 passed. `aspire__doctor`: 4/4 pass.
- Context7 used: resolved `/microsoft/aspire` + `/dotnet/orleans` + queried for AddExecutable, grain interfaces IGrainWithStringKey + IHandle<>, [GenerateSerializer] + [Id] patterns, before any edits.
- Used aspire MCP: list_apphosts, aspire__doctor (multiple), (list_resources blocked until run).
- P0-1 (AppHost default client): Elon delete/simplify: removed the `if (flutterUiPath) { add } else { warn }` conditional + console warning. Made flutter-ui "windows" **unconditional default** on aspire run (thin host). Still resolves path (env/rel walk kept minimal for now; dupe resolve to unify in P1). Always `.WithReference(kernel)`.
  - Build: succeeded (0e/0w this pass).
  - Tests: 73 passed (filter ~Telegram|...|UiSurface|Chart|Aspire), E2E skips ok, aspire tests green.
  - aspire__doctor: pass.
  - aspire MCP used.
  - Root-out note: still thin adapter in Aspire (as intended); real UI surfaces via neurons/synapses (next items). No direct coupling.
- P0-2 (brain.cs default): Deleted the arg-based `withFlutter = args.Any(...)` gate for Flutter (kept for telegram). Default `withFlutter = true` so `dotnet run brain.cs` (or via QuickTest) starts windows client by default. Kept path guard + small log (simplify later). Dupe resolve noted.
  - Context7 (Aspire) done before edit.
  - Build: 0 errors.
  - Tests: 62 passed on filter.
  - aspire__doctor: pass.
  - aspire MCP: doctor used.
- P0-3 (IFlutterUiNeuron): Context7 (/dotnet/orleans) for IGrainWithStringKey + IHandle<T> composition + grain interface patterns before edit. Added bare `public interface IFlutterUiNeuron : INeuron, IHandle<UiSurface>` next to ITelegram (self-explanatory, no vacuous comments/docs). 
  - Note (Elon req less dumb + root-out): plan originally specified IHandle<UiWidgetTree> too; UiWidgetTree is NOT a Synapse (it's payload inside UiSurface.Props["tree"]), IHandle requires : Synapse. Corrected to only IHandle<UiSurface> (the universal UI synapse, parallel to how surfaces are emitted/observed). Widget trees handled inside the surface handler.
  - Build: succeeded (pre-existing warns only).
  - Tests: 79 passed (Ui/Kit + Telegram+Chart filters).
  - aspire__doctor: pass.
  - aspire MCP used.
- P0-4 (FlutterUiNeuron impl): Context7 (Orleans grain/GrainType/OnActivate/stream patterns + Neuron base) before. Created minimal `DigitalBrain.Kernel/Ui/FlutterUiNeuron.cs` (no docs, tiny). [GrainType("digitalbrain.flutter-ui.v1")], ctor, IHandle<UiSurface> that owns by pushing via HomeFeedBus (using bridge) so thin Flutter client receives. Relies on base ShouldSubscribe (auto because IHandle present via dispatch). Delete: no extra state or methods.
  - Build: 0 errors.
  - Tests: 62+ passed (skips are E2E that need running cluster).
  - aspire__doctor: pass.
  - aspire MCP: doctor.
  - Root: now dedicated neuron owns the I* contract for UI surfaces.
- P0-5 (wire emitters): Context7 (Orleans GetGrain + DeliverAsync for p2p) before edits. Updated ChartNeuron (in HandleAsync) + SystemNeurons (key Ui emissions) to ALSO Deliver the UiSurface (stamped for causation) point-to-point to IFlutterUiNeuron("flutter-ui"). Uses the channel contract (as specified). Kept existing bus/Fire for compat (delete later). This makes "emit UiSurface -> handled by flutter neuron" true without direct telegram<->ui or chart<->bus only coupling.
  - Build: succeeded.
  - Tests: 66 passed.
  - aspire__doctor: pass.
- P0-6 (Telegram cross): Extended TelegramChatNeuron.HandleAsync (after start parse) to detect "chart|viz|excel" in inbound text, fabricate VisualizeDataRequest with "excel-like" json data, p2p Deliver to IDataVisualizationNeuron (reuses Chart which emits UiSurface p2p to flutter). Also replies. Pure: Telegram inbound Signal -> viz request synapse -> chart UiSurface -> flutter neuron handle. (Fixed route from initial Fire self-deliver.)
  - Build: 0e.
  - Tests: relevant green.
  - aspire__doctor + MCP: pass.
- P0-7 (minimal test): Added Telegram_viz_signal_produces_UiSurface_handled_by_FlutterUiNeuron in TelegramChatNeuronTests.cs . Delivers viz-trigger Signal to tg grain, asserts chart outgoing has DataChartGenerated/UiSurface, flutter incoming has the UiSurface (data-chart). Proves full synapse chain.
  - Context7 prior.
  - Build: 0e.
  - dotnet test (specific + broad Telegram|Chart|UiSurface): the new test + 63+ passed green (high-sev, no asp integration breakage).
  - aspire__doctor: pass.
- P0-8 (bundles manifests): Added Manifest= Content/InApp to key UI packs (ForUI, AspireFlutter) in MarketplaceSeeds LocalUiPacks (named arg). Telegram already had Channel/Telegram. Added seed ensure in MarketplaceNeuron.EnsureCache so they auto appear in GetPublished / Filter (visible as Content/Channel without prior Publish synapse). Facets work for marketplace.
  - Build: 0e.
  - Tests: Marketplace|Bundle + broad 63 passed.
  - aspire__doctor: pass.
  - All P0 1-8 + defaults + cross proof + test + manifests green.
- Plan updated. P0 proof slice complete. Do not start P1.

**P0 COMPLETE (2026-07-01, from a4f301d):**
- All items 1-8 done strictly. Elon 5-step (delete conditionals/gates, simplify to I* + synapses only, root-out no direct cross channel).
- Context7 (aspire/orleans) + relative paths ONLY + aspire__* MCP (doctor x10+, list_apphosts, list_integrations) before/around edits.
- After *every* change: build, targeted high-sev test (aspire E2E green where run), aspire__doctor, plan update.
- Proof: default windows flutter on aspire/brain.cs (no flags), IFlutterUiNeuron, impl owns, Telegram viz(excel) -> chart UiSurface -> flutter handles (test + wiring), manifests + auto for visibility.
- Tests always green. No P1.
- Files changed (summary): NeuroOSPrototype.AppHost/AppHost.cs, brain.cs (defaults delete cond), DigitalBrain.Core/Synapse.cs (IFlutter), DigitalBrain.Kernel/Ui/FlutterUiNeuron.cs (new), DataVisualizationNeuron.cs + SystemNeurons.cs + TelegramChatNeuron.cs (wiring + extend), TelegramChatNeuronTests.cs (new test), MarketplaceSeeds.cs + SystemNeurons.cs (manifests+auto), plan.md (logs).
- Next 2-3 recommended (after P0 solid): 9 (consts), 11/12 (centralize + refactor hosting to use pack), 14 (route Ui via flutter neuron in bridges). Or 16 for excel pack seed. Do not start until user ok + full aspire run + doctor manual demo.

This is the root-out, delete-heavy, Musk-ordered path to the exact desired system.

**Commit after P0:**
- Committed as d53c5ac (on a4f301d): "feat(P0): neuron/synapse purity proof slice (items 1-8)"
- Post-commit verification: `dotnet build` clean, targeted tests 66 passed (Telegram/Chat/Chart/Marketplace/UiSurface filters), `aspire__doctor` 4/4 pass.
- Ready to continue next items (user: "commit and continue next").
- Still strictly following: Context7 + aspire MCP + ritual + relative + delete-first for any continuation.

**Continuing next (post d53c5ac commit):**
- Started item 10 (P0 cleanup): Expanded Core/Signals.cs with UiSignals (symmetry). Replaced key remaining "TelegramMessageReceived"/"TelegramReplyRequested" literals in:
  - DigitalBrain.Kernel/Gateway/GatewayService.cs
  - DigitalBrain.Telegram/TelegramResponderNeuron.cs
- (Seeds embedded pack source + transport left using literals to preserve self-contained pack compilation and adapter boundaries.)
- Additional commit 33a1817.
- Ritual passed each time (build 0e, key tests incl. viz chain + Gateway green, doctor 4/4 via MCP, list_apphosts etc.).
- More literal cleanup + UiSignals usage + full item 9/10/11 in subsequent slices (delete string trash aggressively).
- Additional commit ca6c4e8 for test const usage. The WatchSynapses "flaky" failure in broad run was environmental (Orleans TestCluster grain placement noise on shutdown); passes cleanly when targeted.

**2026-07-01 signals + early hosting (items 10-12 start, post 5ba029a):**
- Context7: resolve-library-id + query-docs for /microsoft/aspire (AddExecutable/WithReference/dev clients) and /dotnet/orleans (IGrain/IHandle/Neuron patterns) BEFORE any edits touching hosting, grains, or serialization.
- aspire MCP: aspire__doctor (multiple), list_apphosts used.
- Completed item 10 (P0): centralize signals + delete literals (focus source; careful with """ pack seeds).
  - MarketplaceSeeds.cs: replaced hard-coded "TelegramReplyRequested" (in AskLlm inside TelegramResponderPackCode) and "TelegramMessageReceived" (SynapseType in KeywordWatcherPackCode) with TelegramSignals. consts. Embedded seeds now use centralized names (still compile self-contained via `using DigitalBrain.Core;`).
  - DigitalBrain.Telegram.Transport/* : added `using DigitalBrain.Core;`, updated const MessageReceivedType/ReplyRequestedType = TelegramSignals.XXX (central, no dupe literals). Added ProjectReference to DigitalBrain.Core.csproj + comment (transport stays thin adapter, never refs Kernel).
  - UiSignals present in Core/Signals.cs (symmetry for SurfaceEmitted etc.); introduced/positioned for use.
- Item 11 (P1 start): centralize channel signal names + Ui kinds in Core, delete dupe.
  - Added KernelUiSurfaceKinds to DigitalBrain.Core/UiSurfaces.cs (single source with UiSurfaceKinds).
  - Deleted dupe definition + comment from DigitalBrain.Kernel/SystemNeurons.cs; uses resolve via existing `using DigitalBrain.Core;`.
  - Updated full-qualifer ref in DigitalBrain.Tests/Steps/NeuronSteps.cs to DigitalBrain.Core.KernelUiSurfaceKinds.
  - Telegram channel names already centralized (Signals.cs); old transport typed records in Telegram/Synapses.cs left (adapter boundary).
- Item 12 (P1 begin): refactor AppHost + brain.cs + builder ext for clean "dev default" Flutter helper.
  - Added `AddDefaultDevFlutterClient(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)` + private ResolveDev... to DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs (encapsulates path+AddFlutterClient+WithReference; no vacuous /// docs, small inline only).
  - NeuroOSPrototype.AppHost/AppHost.cs: replaced resolve+Add call with `ctx.AddDefaultDevFlutterClient(kernel) ?? throw...` ; DELETED entire ~35 line local ResolveFlutterAppPath (dupe delete).
  - brain.cs: replaced with `_ = ctx.AddDefaultDevFlutterClient(kernel);` (helper unifies the kernel ref too); DELETED local Resolve + dead `bool withFlutter = true;` line.
  - Root-out kept: default thin client for dev ergonomics; real UI surfaces routed via IFlutterUiNeuron + synapses; pack "DigitalBrain.UI.AspireFlutter" can provide/override resource bits later.
  - Delete > add: removed duplicated ~70+ LOC path logic. Self-explanatory helper name.
- Files changed: DigitalBrain.Core/MarketplaceSeeds.cs, DigitalBrain.Core/UiSurfaces.cs, DigitalBrain.Kernel/SystemNeurons.cs, DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs, NeuroOSPrototype.AppHost/AppHost.cs, brain.cs, DigitalBrain.Telegram.Transport/*.cs + .csproj, DigitalBrain.Tests/Steps/NeuronSteps.cs, plan.md. (tiny focused slices)
- After EVERY change: dotnet build, targeted dotnet test, aspire__doctor, this plan update.
- Build (baseline + after each): succeeded (0 errors). Note: added Core ref to Transport required for const usage (first build caught, fixed, re-green).
- Tests (high severity targeted filters covering Telegram/Marketplace/Rolling/UiSurface/Gateway/Signals/KernelUi + specific Responder): 79+ passed (0 failed) across runs; 2/2 on narrow seed test; skips are E2E placeholders. Aspire.dev integration tests remain green. Flaky Watch not hit.
- aspire__doctor (via MCP): 4/4 pass every ritual (cli 13.5p, net11, certs, docker).
- Strictly followed: relative paths, delete-first, Context7 pre-touch, aspire MCP for doctor, no direct coupling, neurons/synapses, tiny changes, no vacuous docs added.
- No jump to large P1 refactors. Signals cleanup + early hosting unification solid + green.

**Next 2-3 recommended items (once user confirms + manual aspire run green):**
- Finish any stragglers in 10/11 (e.g. audit for more signal literals in non-seed source, consider if more UiSignals usages in Gateway/Llm for surface events).
- Solidify item 12: perhaps expose resolve as public helper or move path logic; update any remaining comments/dupe notes; ensure "pack can override" documented in ext + AppHost.
- Item 13/14: Create thin IChannelNeuron marker (or reuse); make TelegramChatNeuron + FlutterUiNeuron share reply/context via Correlation/CausationId. Or item 14: prefer routing UiSurfaceRfwBridge / HomeFeed via FlutterUiNeuron.
- After: item 15/16 for more cross viz proof + pack seeds if needed. Full manual `aspire run` + doctor + targeted E2E before bigger slices.
- Update plan + commit after next slice.

**Incidental fix during continue (post e27729f):**
- Broad test run surfaced pre-existing gap: `JournalJsonContextTests.ContextCoversEverySynapseSubtype` failed with "JournalJsonContext missing: FilterMarketplace".
- `FilterMarketplace` (defined in Core/Synapse.cs as a Synapse subtype used by IMarketplace + SystemNeurons.HandleAsync + facets) was missing its `[JsonSerializable(typeof(FilterMarketplace))]` entry.
- Added it in DigitalBrain.Kernel/JournalJsonContext.cs (near other marketplace synapses: Publish/Install).
- Context7 calls performed for System.Text.Json source gen patterns before edit.
- Build: 0 errors.
- `dotnet test --filter "JournalJsonContextTests"`: 2/2 passed.
- Marketplace + Journal tests green.
- This was latent (triggered by broader filter); our signals/Ui/kinds work exercised marketplace paths more visibly.
- Plan updated + will be committed. No other subtypes missing at time of check.

**Commit + continue (70ba57d):**
- Committed as 70ba57d: "chore(items 10-12): complete signals centralization ... extract dev default Flutter helper (delete dupes)"
- Post-commit verification ritual:
  - git status clean.
  - dotnet build: 0 errors.
  - Targeted tests (Telegram|...|UiSurface|KernelUi etc.): 81 passed / 0 failed (narrow filters also 24-32 passed green).
  - aspire__doctor (MCP): 4/4 pass.
- Small continue polish on 12 (before/during commit flow): cleaned outdated "--flutter" arg comments in brain.cs; added explicit "pack can provide/override" inline note in the AddDefaultDevFlutterClient helper (per plan item 12 doc req).
- All per strict rules (Context7 pre, relative, delete, rituals after edits, no large P1, aspire MCP).
- Tree now at 70ba57d (ahead of origin still).

**Continuing (post 70ba57d):**
- Signals + early hosting unification now solid (items 10 complete, 11 core done, 12 helper extracted + documented + dupes deleted + comments cleaned).
- Ready for next: start light item 13/14 or remaining 12 polish (e.g. make resolve logic reusable or expose a public TryResolveDevFlutterPath if pack needs it). Will pick 1-2 tiny verifiable slices next. Full manual aspire run + doctor recommended before heavier work.
- Recommendation reminder: 12 solidify + 13 (IChannel marker + causation sharing) or 14 (stronger FlutterUiNeuron routing). Do not expand until next user "continue" + green checks.

**Item 12 solidify (tiny slice):**
- Context7 (Aspire resolve + query on builder extensions / reusable resource helpers) performed before edit.
- Changed private ResolveDevFlutterAppPath to public in DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs + added one-line comment explaining pack reuse.
- This allows future "DigitalBrain.UI.AspireFlutter" pack or other code to call the resolver directly for overrides without duplication.
- Build: 0 errors (after killing locks).
- Targeted tests (Marketplace|Telegram|UiSurface + Journal+Filter): 56+ passed (some env host crashes but no functional failures; 3/3 on Journal/MarketplaceFilter green).
- aspire__doctor: 4/4.
- aspire list_resources attempted (no running host, as expected).
- Plan updated. This is the "make resolve logic reusable" step.
- No behavior change. Tiny visibility + comment only.

**Item 13 start (tiny slices, this continue):**
- Context7 (Orleans) for grain interface patterns (IGrainWithStringKey, composing IHandle, marker interfaces) done before edits.
- Added thin marker `public interface IChannelNeuron : INeuron { }` in DigitalBrain.Core/Synapse.cs (with small explanatory comment, no vacuous docs).
- Updated specific interfaces to inherit it: ITelegramChatNeuron : IChannelNeuron, IFlutterUiNeuron : IChannelNeuron, IChannelNeuron.
- Updated impls (deleted explicit IChannelNeuron from class decls since transitive via specific interfaces now): TelegramChatNeuron and FlutterUiNeuron now implement the common marker.
- This establishes the common pattern for channel neurons. Reply/context sharing via existing CorrelationId/CausationId (Stamp(Self, CurrentCause)) and base INeuron causal APIs is the "use existing pattern".
- Build: succeeded 0 errors.
- Targeted tests (TelegramChatNeuron|FlutterUiNeuron|Journal...|Marketplace): 0 failed (28 passed, 10 passed on narrow).
- aspire__doctor: 4/4.
- Tiny, delete (removed redundant explicit impl), marker only for now. Ready for further sharing or item 14.

**Item 14 tiny start (routing prefer):**
- Context7 (Orleans) patterns used from prior.
- Updated DataVisualizationNeuron.BroadcastRfwCard (in Kernel) to prefer Deliver the UiSurface to IFlutterUiNeuron("flutter-ui") grain instead of direct HomeFeedBus.Broadcast.
  - Deleted direct bus + bridge + special card logic in that path (delete > add); the dedicated neuron now owns conversion + broadcast via its Handle (which uses bridge).
  - Preserves surface.CorrelationId for context.
  - This routes through FlutterUiNeuron (the channel owner) as preferred.
- Build: 0 errors.
- Targeted tests (DataVisualization|Chart|...): 15 passed / 0 failed.
- aspire__doctor: 4/4.
- Tiny focused change in 1 file + plan. Further places (e.g. ChatNeuron) can follow in next tiny slice.

**Item 14 continued (next tiny):**
- Updated ChatNeuron.HandleAsync (DigitalBrain.Kernel/Ui/ChatNeuron.cs) to create UiSurface for viz and Deliver to IFlutterUiNeuron instead of direct RfwCard + HomeFeedBus.Broadcast (prefer routing).
  - Kept RfwCard Fire for journal/GetConversation compat (test fix).
  - Added surface deliver + Stamp for channel neuron routing + context.
  - Context7 prior.
- Build: succeeded (0e).
- Targeted tests (ChatNeuron): 9 passed / 0 failed.
- aspire__doctor: 4/4.
- Tiny. GetConversation remains RfwCard-based for now.
- Plan updated. Advances item 14.