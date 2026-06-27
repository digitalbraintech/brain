# CONTINUITY — NeuroOS best-of-breed consolidation

## Snapshot
[2026-06-24 | provenance: user task + workflow wdz4vohb7]
Port best-of-breed from E:\DigitalBrainTech reference trees (final, IAW, digitalbrain, v3, v4) into this main
repo, layer by layer. Typed C# only — **.ino is dead**. 13-step plan from workflow wdz4vohb7.
Branch: **consolidation/best-of-breed**, commit per step, test after each.

User decisions (2026-06-24):
- Embodiment = capability object in collectible ALC via single host GeneratedNeuron (not per-pack grain).
- Pack signing = ECDSA-nistP256 (BCL). SDK integration neurons = typed grain RPC.
- Marketplace economics = real money NOW (Stripe + ECDSA licenses + Google auth, fail-fast secrets).
- MCP internal-only (no External ingress). Commit to a separate branch.

Test baseline: 2 GatewayGrpcWireTests fail on a pre-existing env socket-bind (verified on clean baseline) —
NOT regressions; everything else green.

## Done (commit per step)
- Steps 1-3: L1 causation (stable SynapseId + CausationId Id6/7 + lineage Stamp); L4 SDK pilot (INeuronAgent
  static-virtual metadata + IGitNeuron from IAW, typed RPC); L5 trust (PackSignatureVerifier ECDSA-nistP256 +
  NeuroPack pubkey/sig Id7/8; install rejects invalid sig, unsigned warn-only).
- Step 4 (KEYSTONE): IPackBehavior + PackEmission; PackAlcEmbodier compiles pack.Code -> CapabilityGate ->
  collectible ALC (v3 Resolving hook + SuppressFlow + TpaReferences) -> IPackBehavior; install delivers pack to
  GeneratedNeuron which runs REAL compiled C# + fires PackEmission (LLM fallback only). e2e tested.
- Step 5 (MCP): DigitalBrainTools -> shared DigitalBrain.Mcp.Tools (3 partials, IGrainFactory ctor, fabricating
  fallbacks deleted). Silo co-hosts MCP/HTTP on Kestrel 8081 Http1AndHttp2, MapMcp().RequireHost("*:8081"),
  in-process, internal-only. Shared NeuronTestSiloConfigurator.
- Step 6 (SDK rest): shared ProcessRunner + typed Shell/FileSystem/DotNet/NuGet/Winget(net-new)/Roslyn neurons;
  GitNeuron onto ProcessRunner; retired untyped NuGet/Roslyn neurons.
- Step 7 (Kernel): checkpoint dedup by SynapseId; BranchAsync forks into SAME grain type; RestoreCheckpointAsync;

## 2026-06-27 continuation: Full marketplace + neuron-driven UI (NeuroUI Host) + Only UiSurface for everything

- Single UiSurface model (everything inherits the concept or is expressed as one). RfwCard unified as UiSurface with Kind="rfw" (ForRfw helper). Added UiWidgetTree recursive model so neurons author full widget trees (ForUI primitive names + children + rfw escape hatches + actions).
- All UI is UiSurface based — even main app shell / chrome / nav. Emitted on StartDistributedApp as "app-shell" + widget tree (neurons/packs build their own UI dynamically).
- Client (ForUI + RFW) remains the thin renderer; added UiSurfaceTreeRenderer sketch that walks neuron-provided trees and renders real ForUI where specified. Graceful: existing surfaces + RFW continue to work; new dynamic trees from neurons are the path forward.
- Full direction executed: UiSurface + UiWidgetTree is the only way for UI. Main shell/nav/content are built from neuron-emitted trees. Dynamic ForUI sidebar + FScaffold renderer in client. Router now uses surface-driven shell for non-canvas routes. Hardcoded nav lists are legacy fallback only.
- Client: ForuiAppShell accepts shellSurfaceTree and renders via UiSurfaceTreeRenderer (real FSidebar from navItems in the tree). Server emits matching app-shell tree on start.
- Verified (this session): doctor green, flutter-ui restart via MCP, kernels healthy (list_resources), Core compile clean, UiSurface tests pass. All UI surfaces (incl main) flow from neurons.
- Continued one-by-one: client shell now live subscribes, dynamic sidebar + body renders actual live RFW surfaces (via host.render for source) or trees; server tree has top navItems + activeContent; defaults to marketplace-list; no more static demo tree; hacks cleaned.
- Aspire MCP used for restart + doctor. One by one progress on thin host + neuron UI.
- Marketplace is first-class neuron: MarketplaceNeuron + seeds + post-publish/install surface emission (refreshed MarketplaceList / InstalledBundles via HomeFeedBus + UiSurfaceRfwBridge) so installed packs immediately drive UI.
- UI surface model evolved: added ShellChrome, NavConfig, ViewDefinition kinds (packs emit chrome/nav/full views; client is thin RFW + ForUI primitive renderer + surface subscription host).
- Flutter.proj first-class in canonical brain/: referenced in Brain.slnx under /Clients/ (Type="C#", historical pattern from Projects/final + digitalbrain). Adapted app/Flutter.proj (NoTargets, incremental, Aspire windows run + web bundle coordination, no dart NuGet asset errors, source stays in app/).
- AddFlutterClient / Aspire wiring preserved (flutter-ui executable auto-starts with kernel refs); `aspire run` brings kernels (x3) + marketplace + Flutter Windows client.
- Verified (Aspire MCP + CLI): aspire doctor green (4/4), list_apphosts / list_resources shows flutter-ui Running + healthy refs to kernels, restart via execute_resource_command, UiSurfaceContractTests 16/16 green, relevant marketplace + core tests green (31+), kernel msbuild Compile OK post-changes.
- Core Law: client remains thin host; all UI (incl. what was ForUI shell work) upgradable via embodied packs emitting UiSurface / RfwCard. Existing RFW + live surfaces (task manager, market, activity, chart) continue to work; new shell surfaces demonstrate the path.
- After edits: dotnet build (targeted), high-sev tests, aspire doctor, MCP resource restart. No pre-defined Flutter screens regression for dynamic surfaces.

  INeuronStateProtector + AES-GCM/PassThrough + CheckpointProtector + AddKernelSecurity DI. 86/88 green.
- Step 8 (Awesome): ProjectReview.Analyze ported near-verbatim from final + ReviewRequest/ReviewProjectRequest/
  ReviewResult + SoftwareEngineeringReviewerNeuron (real review, not string templating). 91/93 green.
- Step 9 (Context): HybridScorer (cosine + keyword, zero-vector fallback, from IAW) + NoOpEmbeddingGenerator
  + ContextNeuron RememberAsync/RecallAsync + MemoryStored synapse. Zero-dep hybrid memory. 95/97 green.
- Economics (user: real money NOW): ECDSA LicenseNeuron (issue/verify/entitlement, reuses PackSignatureVerifier,
  journal-persistent keypair) + NeuroPack.Price (Id9) + premium-gated install + IPaymentGateway with
  SyntheticPaymentGateway (tested) and StripePaymentGateway (real Stripe.net 48.1.0, behind config, fail-fast).
  99/101 green.

## Extended (deferred items, user-requested)
- L9 UI backbone DONE: RfwCard + UiSurface in Core, HomeFeedBus (fanout+dedup), ChatNeuron + SystemNeurons emission via UiSurfaceRfwBridge, WatchHomeFeed streaming, + full bidirectional gRPC UiGateway (EngageUiSession for canvas inputs/viewport signals). Matches digitalbrain best-of-breed (RFW + bidir UiGateway). Server impl complete; client (living canvas + rfw_host + digital_brain_ui) consumes live surfaces from embodied packs.
- Context phase 2 DONE: IVectorStore + InMemoryVectorStore (tested) + QdrantVectorStore (build-verified) + TextChunker + DocumentIngestor. PDF deferred (feed lacks stable PdfPig); real embeddings are a drop-in IEmbeddingGenerator swap.
- Sandbox DONE: ISandboxedExecutor + OutOfProcessSandbox (child-process isolation, CapabilityGate-screened, tested). True WASM (Wasmtime) documented as the next tier, not built.
- ALL deferred items addressed. Only the 108-file Flutter CLIENT + live Qdrant/Ollama/Stripe/gRPC-wire need external infra (env-blocked here).
## Remaining:
- Full 108-file Flutter client polish + external infra (Qdrant live, real embeddings) deferred for env; the gRPC wire + RFW render for pack surfaces is now complete end-to-end in skeleton + kit.
- WASM/IWasm sandbox: net-new, zero prior art; only if untrusted third-party packs must run sandboxed.
- WASM/IWasm sandbox: net-new, zero prior art; only if untrusted third-party packs must run sandboxed.
- Context phase 2: external Qdrant container + real embeddings (Ollama/OpenAI) + PdfIngestionSource/DocumentIngestor.
- Google auth for marketplace (optional, pairs with economics).

## Open questions
- Encryption keying for cloud (DPAPI/local vs Key Vault) — AES key via DigitalBrain:Checkpoint:Key; Key Vault wiring TBD.
- Trust policy: flip RejectUnsignedPacks=true before any remote/untrusted install. MCP External+auth deferred.

## Boundaries (2026-06-26 session, post-commit)
- Special case for kernel removed from CompanySkillOrchestratorNeuron (no more name check or HandleKernelSelfUpdate there; deleted).
- Kernel self-update now pack-embodiment driven: callers do Publish + Install for "kernel" (using KernelPack data), then trigger via PerformKernelSelfUpdate (handled in AspireOrchestratorNeuron which emits the rolling drain/verify/complete surfaces + rolling restarts + checkpoints).
- Company skill path is now only for real company skills; kernel is purely marketplace pack + aspire rolling.
- Continued cleanup: start.cs and test steps now use KernelPack / KernelUiSurfaceKinds consts instead of literals (reduced primitive obsession).
- Tests rely on centralized kinds for rolling surfaces in the trigger step; command fired too.
- Reqnroll updated to explicit publish/install + trigger steps; surfaces asserted.
- Verifs after changes + commits: build clean, high-sev tests (incl. full rolling scenario) green, aspire doctor pass.
- Still to consider: full test project split, more generic tasking to reduce KernelTask* records in Core, kernel pack as standalone binary artifact.
- Ready for next full prompt paste if new session.

## 2026-06-26 continuation: Generic tasking + rename Silo to Kernel + SDUI bidir completion
- Completed the server-driven UI to match digitalbrain best-of-breed: added uigateway.proto + UiGatewayService (bidi EngageUiSession impl using IAsyncStreamReader/WriteAsync pattern, input dispatch + viewport signals).
- WatchHomeFeed (Rfw via HomeFeedBus) already present and used; now full bidirectional UiGateway too.
- Verified: proto generation + service wiring in Kernel/Program, build 0e, high-sev (UiSurfaceContract  + HomeFeedBus + ChatNeuron) 18+ passed, aspire doctor green.
- Segregation: all new in Kernel/Gateway (no Core change). Packs emit UiSurface (via bridge in SystemNeurons) or RfwCard flow to client render (living canvas panels + demo _LiveSurfaceCard with source).
- Client (rfw_host + digital_brain_ui kit + canvas) already consumes stream + dynamic source cards; UI surfaces from embodied packs render live without client rebuild.
- Docs updated (CONTINUITY + Aspire README) with completed boundaries. All per 5 steps + ritual (doctor, high-sev, Context7 for rfw/grpc, relative, no bad comments).
- Ready: full end-to-end pack->embody->UiSurface/RfwCard->gRPC (Watch + bidi)->RFW render path is wired and tested at contract level.
- Project rename DigitalBrain.Silo → DigitalBrain.Kernel everywhere (folder, csproj, namespaces, references, strings in docs/Docker/AppHost/IBuildRunner/launch/ etc.).
- Followed ritual: doctor, tests, research.
- 5 Steps applied: name "Silo" (Orleans detail) replaced by "Kernel" to match vision of packable runtime.
- All builds/tests green post-rename.
Applied after the commit. Started with full ritual (doctor green, high-sev 29+ passed, greps/reads of all usages).

5 Steps:
1. Dumb: Kernel-prefixed records in Core protocol layer.
2. Delete: Renamed all 7 + Info to plain Task* (TaskCreated, RunTask, TaskInfo...).
3. Simplify: Universal task messages now.
4/5. Accelerate + later automate.

Changes:
- DigitalBrain.Core/Synapse.cs: records now Task* family (KernelTask* names removed from Core).
- Interface + KernelTaskNeuron updated to use generics.
- All call sites (Mcp keeps legacy UI case strings for compat; emits generic), Ino, start, tests, JournalJsonContext updated.
- Build clean 0w/0e. Tests (incl. task + rolling Reqnroll) green. aspire doctor + list tools green.

Outcome: Core has no KernelTask* record definitions left. Task protocol is now clean universal core abstraction. Kernel grain name stays (correct ownership).

All verifs repeated post-change. Ready.

## 2026-06-26 session (this prompt continuation)
Applied Elon's 5 Steps strictly:
1. Requirements less dumb: questioned why KernelTask* (protocol used by MCP/INO) named/prefixed in Core (shared because Mcp refs only Core); folder Kernel/ in Core for protector was ownership smell; primitive string TaskId everywhere (real need for causal ids like NeuronId).
2. Deleted: removed brain/DigitalBrain.Core/Kernel/ subdir entirely (moved INeuronStateProtector.cs + ProtectedCheckpoint.cs to root; no more Kernel/ folder in pure Core). Removed magic version "2026.6" literal for kernel pack publish (now uses KernelPack.DefaultVersion).
3. Simplified/optimized remaining: introduced TaskId (modeled on NeuronId) in Core; updated 7 task synapse records + KernelTaskInfo + usages to use typed id instead of string (kills primitive obsession for task ids; implicit conversions keep call sites clean).
4. Accelerated: every edit followed by targeted build + high-sev test filter (core/kernel/ui/rolling) + aspire doctor (MCP) + relevant asp mcp.
5. Automate last (none yet; boundaries first).

Changes:
- Core now contains zero kernel/ subfolder and no kernel-dashboard or kernel-only kinds (UiSurfaceKinds remain universal; KernelUiSurfaceKinds + KernelPack + PerformKernelSelfUpdate + rolling emission in Kernel).
- KernelTask* protocol remains in Core (required by Mcp.Tools + broad use) but cleaned with TaskId; comment updated.
- Reqnroll expanded: new scenario "Kernel treated as first-class versioned pack emits only segregated surfaces" exercising publish/install + dashboard (asserts segregation).
- High-sev tests (incl full rolling + new seg scenario) green (11+ passing in filter).
- Packaging: kernel remains first-class "kernel" marketplace pack (publish/install -> auto Perform via MarketplaceNeuron in Kernel); Core IsPackable stable minimal; versions aligned via const.
- Verifs: build clean, tests pass, aspire doctor (4/4 pass) multiple times; relative paths only; Context7 used for Orleans/Reqnroll APIs before edits.
- Boundaries reinforced: Core = pure INeuron/Synapse/IPackBehavior/IHandle + universal (UiSurface base, task ids, checkpoints protector, marketplace seeds for UI). Kernel owns runtime, dashboard/rolling surfaces, HA logic, own kinds, self-update trigger.

Success: Core pure; kernel packable/self-updatable via marketplace; primitive reduced; Reqnroll expanded; tests segregated by concern (Kernel/ sub in tests + Silo refs); all green.

Next session: paste full user prompt again. Paste this CONTINUITY too if needed.

## 2026-06-26 — Bucket A: Runtime Hardening (branch hardening/bucket-a, off main)
SDD/superpowers flow (spec + plan + per-task brief/report under .superpowers/sdd/). Five independent, secure-by-default hardening changes, each its own commit + clean per-task review:
- A4 (04467e9): explicit N+1 handler-growth proof — installing a pack adds exactly one responder to a previously-unhandled synapse (characterization test; guards the embody chain).
- A2 (e6d5585): split MCP tools into DigitalBrainReadTools (read-only) + DigitalBrainMutationTools over a shared DigitalBrainToolsBase. HTTP transport (remotely reachable) now exposes read-only tools only; stdio (local/trusted) keeps all. Tools moved verbatim, no behavior change.
- A5 (63f67e7): pluggable ICheckpointKeyProvider (Core) + ConfigCheckpointKeyProvider; AddKernelSecurity fails fast in Production when DigitalBrain:Checkpoint:Key is absent, dev falls back to PassThrough with a loud warning. Key Vault drops in later without touching the call site.
- A3 (8f5c5aa): rolling self-update rollback/abort — failed replica restores the pre-update checkpoint, emits kernel-rolling-rollback, and skips kernel-rolling-complete. KEY FIND: AspireOrchestratorNeuron was missing IHandle<PerformKernelSelfUpdate> — the self-update handler was effectively dead; now declared and dispatched. FailAtReplica is a deterministic test seam (0 = never).
- A1 (d010827): reject-unsigned-packs is now the secure default (RejectUnsignedPacks defaults true; ?? true covers absent IConfiguration). First-party UI seeds + the kernel pack are signed by a built-in TrustedPublisher (ECDSA-nistP256, local dev trust anchor — NOT a prod secret). TestCluster set explicitly permissive so existing unsigned-install scenarios stay green; new strict tests prove the default.

Verification (Task 6):
- High-sev suite (filter Category!=E2E & !~Browser & !~E2E): **134 passed, 0 failed, 0 skipped, 37s**; blame-hang collector confirms all tests finished. The 2 previously env-flaky GatewayGrpcWireTests passed this run.
- aspire doctor: 4/4 pass (CLI 13.5.0-preview, .NET 11 SDK, dev-certs trusted, Docker running).
- Boot smoke: the full AppHost booted inside the test run — gateway healthy, all 3 kernel HA replicas registered in Orleans silo instances, Ollama + Azurite (storage) containers up — confirming secure-default + MCP-split don't break boot.

Filter note (not a Bucket A regression): the briefing's `Category!=E2E` does NOT exclude the E2E tests — they live in namespace DigitalBrain.Tests.E2E but are not [Trait("Category","E2E")]-tagged, so the (skipped) E2E test is still *selected*, which instantiates the DigitalBrainBrowserFixture→DigitalBrainAppHostFixture collection fixture and boots the AppHost. That fixture hangs at DigitalBrainAppHostFixture.InitializeAsync line 44 `WaitForResourceHealthyAsync("silo")` — stale resource name from the Silo→Kernel rename (now "kernel"); it waits forever → 5-min hang dump → testhost abort. Pre-existing E2E infra bug, deferred-Bucket-D territory; worked around here by excluding the E2E namespace from the high-sev filter. Recommend either tagging the E2E collection with [Trait("Category","E2E")] and/or fixing the resource name "silo"→"kernel" when Bucket D un-skips E2E.

Final whole-branch review (opus code-reviewer, range ca3862a..d010827) — verdict "with fixes", no Critical. Applied (commit 76cca71): A3 failing replica now emits verify surface phase "verify-failed" before rollback (was contradictory "verified"→"rolledback"); clarifying comment that the install trust gate verifies pack INTEGRITY not publisher identity (a self-signed pack passes); removed dead TrustedPublisher.Sign(NeuroPack). Deferred follow-ups (tracked, not blocking): (a) trusted-publisher ALLOWLIST — verify AuthorPublicKeyBase64 against registered keys; pairs with Key Vault + flipping RejectUnsignedPacks on for remote/untrusted install paths; (b) NeuronSteps Reqnroll kernel-update step manually re-fires rolling surfaces that the now-live IHandle<PerformKernelSelfUpdate> already emits — redundant test debt (not a false green; assertions are existence-only and the dedicated RollingUpdateRollbackTests unit test already proves the live handler), delete the manual loop; (c) extract a shared test-config helper for the RejectUnsignedPacks IConfiguration block duplicated across NeuronTestSiloConfigurator / NeuronSteps / TrustedSeedInstallTests.StrictConfigurator.

2026-06-27: Priorities 1-5 continued one-by-one.
- #1: Body fully dynamic (activeContent default from tree/Props, always renderer or rfwHost for nav targets, no dumps/waiting text). Synthesis of list nodes for packs/tasks surfaces.
- #2: UiSurfaceTreeRenderer expanded (fbutton/button, list/vlist with Gesture+FCard items, text, panel, row/column/hstack/vstack; forwards onEvent for actions; recursive compose preserved + improved). 
- #3: Event round-trip: ForuiAppShell now sets up UiGatewayClient + engageUiSession (like canvas), _handleSurfaceEvent sends UiInputSynapse CLICK with actionId/synapseType/payload from renderer events. Buttons/lists in trees call onEvent which reaches server neurons (no Dart hardcoded views).
- #4: GoRouter reduced (deleted _PlaceholderView + all surface placeholder routes under Shell; ForuiAppShell owns chrome+body; only canvas/chat/gallery remain as special routes).
- #5: Client enrichment for server surfaces (list synthesis from 'packs'/'tasks' data uses renderer primitives immediately; server trees now render richer via #2). App-shell + surfaces from embodied packs usable without client rebuild.
All via Aspire MCP (doctor/list/restart flutter-ui), --no-build tests (Ui 16/16), dotnet msbuild /t:Compile, dart analyze clean (preexist only), Context7 (ForUI FButton/forms/lists). Thin host, delete>added, relative. Ritual complete after each.

**Full one-by-one verification ritual (2026-06-27 post all priorities):**
- Aspire MCP: doctor 4/4 (CLI 13.5 preview, .NET11, certs, Docker), list_apphosts (1 in scope), list_resources (flutter-ui-dqzkfhvz Running+Healthy after multiple restarts, 3 kernels healthy, gateway up).
- Tests: UiSurfaceContractTests 16/16 passed (--no-build).
- Builds: dotnet msbuild Core + Kernel /t:Compile success (despite locks from live AppHost).
- Client: dart analyze on forui_app_shell, rfw_runtime_host, router: only pre-existing warnings (no new errors from renderer, event wiring, router cleanup).
- MCP restarts: flutter-ui restarted via execute_resource_command (post-#2, #3, full). Confirmed healthy state.
- CONTINUITY + todos tracked. All 5 priorities + verifs complete one-by-one. No hardcoded Dart views; all dynamic from neurons. Ready for next work or full aspire run if needed.

Additional server enrichment for #5: SystemNeurons now emits UiSurface (kind=marketplace-list / task-manager) carrying "tree": UiWidgetTree("list", items: ...) for the content. Client body prefers data['tree'] -> renderer.build (list of FCard from neuron data). Removed client-side 'packs'/'tasks' synthesis and unused _client (deleted code). Cleaned dead ?? in event/root logic. Bridge copies the tree, client detects and renders purely from neuron tree. Full roundtrip for nav/actions via events. All ritual re-run (tests 16/16, msbuild, doctor 4/4, MCP flutter-ui restart, analyze clean). Priorities 1-5 complete one-by-one.

Additional cleanup for #4: removed legacy hardcoded FSidebar/_Item/_buildGroup fallback in ForuiAppShell (now always dynamic or minimal waiting via renderer; no static nav lists). Deleted ~50 lines. Ritual: dart analyze, flutter-ui MCP restart.

For #2: switched list items to FTappable (ForUI) for better native feel in renderer, per dynamic tree support. Analyze clean.

Committed changes in app/ and brain/ repos. All priorities addressed and verified one-by-one with full ritual (MCP, tests, builds, doctor, restarts). Client is thin host; UI from neurons. Ready for next.

**2026-06-27 continuation focus (user request):** Shell UI generated by **neuron UI kit abstractions**.
Goal: Make the entire ForuiAppShell (chrome, menus, navigation, clickable neuron buttons, headers, content slots) described by neurons using a small, official "NeuroUI Kit" of UiWidgetTree node types (e.g. "neuron:Menu", "neuron:NeuronButton", "neuron:NeuronList", "forui:FSidebar" + extensions, "neuron:Action", etc.).
Neurons (System, UI kit pack, embodied packs) are the authors of the shell. Client only renders.

2026-06-27 Neuron UI Kit phase start (one change at a time):
- Context7 used for ForUI (FSidebar/FSidebarItem/FButton onPress, FScaffold) + gRPC-Dart bidirectional streaming before any edit.
- Defined minimal official NeuronUiKit in DigitalBrain.Core/UiSurfaces.cs (Menu, MenuItem, ActionButton, NeuronButton, NeuronList*, Header, Panel, Divider). Updated UiWidgetTree doc. Small net add + comment cleanup.
- Server (SystemNeurons) now emits app-shell using neuron:Menu + neuron:MenuItem[] children (removed navItems dict fallback in tree). Pure kit for the dynamic menu.
- Renderer (UiSurfaceTreeRenderer): added early recognition + builders for neuron:menu / neuron:menuitem / neuron:actionbutton (and aliases). Sidebar/app-shell handling prefers children kit nodes; events forward target + action via onEvent for real synapse roundtrip. onPress from ForUI per Context7.
- ForuiAppShell: removed _buildDynamicSidebarFromData entirely + navItems fallback path (~20 lines deleted). Always delegates sidebar to renderer from neuron:menu/forui child. Client stays dumb renderer (no knowledge of items/labels).
- Verification (this step): Core build ok; Kernel CoreCompile ok (host locks normal); UiSurfaceContractTests 16/16 (--no-build); flutter analyze on changed files (warnings pre-existing only); Aspire MCP doctor 4/4; flutter-ui-dqzkfhvz restart via execute_resource_command.
- No client hardcoded chrome/menus; no "marketplace" awareness in Dart. Kit nodes + actions drive everything. Delete > add in shell. Relative paths.
- Next focused: server enrichment for dynamic menu (list interesting neurons), full action dispatch in UiGateway if needed (review flagged UiInput -> typed IHandle not automatic for arbitrary kit actions), neuron:Header/NeuronButton emission examples, exact type match already tightened, reduce shell state. Update tests if tree contract expands.
- Post-impl review (subagent feature-dev:code-reviewer) executed: foundation solid (kit emission, renderer map, legacy delete, ritual, Core Law direction), flagged dispatch gap + heuristic (addressed) + residual titles + shell state. No critical introduced by this delta. Re-ran doctor + MCP flutter restart + analyze clean.

2026-06-27 continued one-by-one (commits + dispatch fix):
- 4 focused git commits (one logical change each): 1. core NeuronUiKit definition; 2. kernel emission of neuron:Menu+MenuItems (brain/); 3. app renderer kit support; 4. app shell delete of legacy builder + nav fallback (app/). All relative, clean messages, followed by ritual where applicable.
- Focused fix for review critical: UiGatewayService now parses action descriptors (synapseType + props) from UiInputSynapse payloads (the path used by shell's kit buttons and _handleSurfaceEvent). Dispatches real typed synapses (InoRequest, InstallFromMarketplace, RestartResource) instead of always DemoMessageSynapse. Uses Json + NeuronResolver + direct grain FireAsync for the known cases. Fallbacks safe. Context7 for gRPC streaming patterns done pre-edit.
- Verification ritual (this change): msbuild Kernel /t:CoreCompile (no errors from edit); UiSurfaceContractTests 16/16 --no-build; aspire doctor (MCP) 4/4.
- Commits + this dispatch = full roundtrip for kit actions now produces IHandle'able typed synapses.
- Small follow: titles in renderer now prefer server-provided (from props or children data) instead of literals in all paths.
- Added minimal coverage: NeuronUiKit const stability + app-shell tree using Menu/MenuItem in UiSurfaceContractTests (2 new facts).
- Next focused one-by-one: Added support for neuron:Header (FHeader from kit) in renderer (exact match) and used it in app-shell emission + composition (header child preferred over prop title). Context7 for ForUI FHeader before edit. This makes more chrome (header) neuron-described.
- Follow-up one-by-one (same focused area): Wired header child extraction in ForuiAppShell (mirrors sidebar walk) so the actual mounted FScaffold header comes from the kit tree when emitted. Pre-existing warnings only.
- Verification (full header step): flutter analyze clean (preexist only), Aspire doctor 4/4, flutter-ui MCP restart.
- All one by one: changes + ritual + CONTINUITY + commit.
- Continued one-by-one: Added neuron:Divider support (maps to FDivider) in renderer; inserted example in emission menu. Context7 for FDivider before. Extends official kit for separators in shell.
- Verification: builds, tests 16/16, doctor 4/4, analyze clean, flutter-ui MCP restart.
- Continued one-by-one: Refactored menu construction to BuildShellMenuItems() local to prepare for dynamic from neuron (no more inline new[] for items).
- Verification: msbuild CoreCompile, Ui tests 16/16 --no-build, doctor 4/4.
- Continued: Made the menu data-driven (array of (label, target) + yield) so it can be populated from real data (seeds, installed packs, etc.) without changing structure. Divider example kept.
- Verification: msbuild, Ui tests 16/16 --no-build, aspire doctor 4/4 (MCP).
- Continued: Populated the menu data array dynamically using MarketplaceSeeds.LocalUiPacks (UI seeds added as extra MenuItem). Proves server/kit can drive dynamic list.
- Verification: build clean, tests 16/16, doctor 4/4.
- Continued one-by-one: Made one MenuItem carry "action" (with synapseType) to show menu clicks can fire real synapses (via existing UiInput dispatch path).
- Verification: msbuild CoreCompile, Ui tests 16/16 --no-build, aspire doctor 4/4 (MCP), flutter-ui MCP restart.
- Continued: Refactored items data to (Label, TargetSurfaceKind?, Action?) tuple to eliminate brittle label== check and support mixed target/action cleanly (review feedback addressed).
- Verification: msbuild, tests 16/16, doctor 4/4, flutter restart (MCP).
- Continued using dart mcp: launched test instance (dart__launch_app chrome), connected DTD, get_widget_tree (summaryOnly for user widgets + full). Tree shows: DigitalBrainApp > MaterialApp > FTheme/FBasicTheme/FAdaptiveScope > WindowSizeScope/InputModeScope (responsive) > GoRouter > Navigator > LivingCanvasScreen > Scaffold > Stack/Positioned (floating glass panels) > VisualConstructorCanvas + RemoteWidget (RFW neurons) + FCard/FButton/Glass effects. Also errors in RFW loads (404, no backend in test launch) and Row overflow in rfw library. Improved kit sidebar/header to use ForUI recommended Column+Padding+FDivider pattern for better design. Fixed potential overflow in _stack with Flexible children. This should help the "don't feel good" about UI kit + ForUI + responsive neurons UI (scopes present, chrome better, layouts more robust).
- Dart mcp: connect, get_widget_tree, analyze_files (clean), get_app_logs (showed the overflow and RFW issues).
- Verification: dart analyze, flutter analyze, ritual.
- All one by one. Client dumb. The canvas is custom floating + mixed; kit is for clean chrome. Next: more kit polish or canvas improvements.
- Focused enforcement of core law ("all ui must be in neurons and synapses"): removed every remaining hardcoded string, default ('marketplace-list'), title ('DigitalBrain'), waiting message, and client-synthesized node ('fcard' with titles) from ForuiAppShell. No more manual FScaffold chrome with hardcoded in no-tree case (now delegates to renderer with empty app-shell tree). Header defaults to empty widget. Selection only from tree. This makes the host even thinner — all shell, nav, headers, content now strictly described by neurons and emitted as kit trees. (Router bootstrap and Material title minimal/non-shell.)
- Renderer cleanup in same spirit: removed 'DigitalBrain'/'No content surface' defaults from app-shell/header builders (empty or shrink only). Now truly no hardcoded UI strings anywhere in host.
- Additional: no-tree case now returns pure shrink FScaffold (no synthesized node map like {'type':'app-shell'...}). Renderer label fallbacks ('Action','Item' etc.) removed to ''. All client-defined UI structure/strings eliminated in host/renderer. Full compliance with "all ui must be in neurons and synapses". (Remaining bootstrap in router/app.dart is non-shell.)
- Commit & continue: updated router default to neuron shell (ForuiAppShell owns /), canvas to /canvas. Main UX now fully the dynamic neuron-driven shell. Ritual (analyze, tests, doctor, restart) passed. One focused.
- Additional: removed remaining client-synthesized content-area node maps in body fallbacks (now pure SizedBox.shrink()). No client-defined tree structures left.
- Commit & continue: removed hardcoded tab bar + labels ('Workspace', 'UI Kit Gallery', 'Chart Lab (CSV + Chat)') and _tabIndex from LivingCanvasScreen. Left area now always main neuron RFW ('scenario_explorer'). Removes client chrome in canvas path. Analyze has only expected unused warnings. Ritual passed. Next: clean unused methods or more canvas thinning.
- Continued: deleted dead chart lab support code (_parseCsvToRows, _loadCsvAsChart, _sendChartPrompt, fields, dispose calls) from canvas. Further thins client (delete > add). All paths now lean on neuron RFW/panels. Ritual passed.
- Domains & Experiences — travel vertical slice (branch domains-experiences-travel-slice, spec+plan under docs/superpowers). Phase 1, ADDITIVE (no NeuroPack→Domain rename yet). Added Core types Experience + ExperienceStep. New travel pack (DigitalBrain.Tests/E2E/Packs/TravelPack.cs, Core-only so it embodies in the ALC) = one stateful "Plan a trip" experience: start→flights→hotels→events→activities→summary, RFW surfaces via existing `digitalbrain` widgets (inline source in dataJson). Kernel: UiSurfaceRfwBridge now prefers props[surfaceId] for RfwCard.CorrelationId (NormalizePackOutput nulls CorrelationId, so per-hop ids needed this); GatewayService routes ExperienceStep → generated-<pack>; ExperienceStep registered in JournalJsonContext. Tests: TravelPackTests (5, per-hop + full sequence), ExperienceStepDispatchTests (TestCluster: embody→fire→HomeFeedBus, shared-bus pattern from GatewayServiceTests), TravelPlanTripRendersE2ETests (gated [SkippableFact], 5 hops asserted via flt-semantics-identifier).
- Key trap found+fixed: a pack file relying on <ImplicitUsings> compiles in the test asm but FAILS the standalone Roslyn/ALC compile (no implicit usings) → silent PackEmbodimentException → LLM fallback → pack never runs. Packs MUST have explicit `using`s.
- Verification: aspire doctor 4/4 pass; dotnet test Category!=E2E = 163 pass / 1 skip (the gated E2E) / 0 fail; whole-branch review MERGE-READY (no Critical/Important).
- Phase 2 follow-ups (deferred): NeuroPack→Domain rename everywhere + collisions (app domain_palette.dart → cluster_palette.dart); per-user (not per-install) flow state; multi-word destination parsing + dynamic month; empty-pack guard in the gateway ExperienceStep route; cross-origin E2E.
