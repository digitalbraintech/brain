# SystemNeurons Bloat Delete and Surface Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Fresh implementer + reviewer subagents per task where code changes; final whole-branch review.

**Goal:** Delete demo bloat and hardcoded surface emission from `AspireOrchestratorNeuron.HandleAsync(StartDistributedApp)` in `SystemNeurons.cs`; move ownership of surfaces to the concerned neurons (or minimal emission points) so the system demonstrates pure "everything is a Neuron or Synapse" with no special-cased god-object startup trees. Preserve observable default UI surfaces and all test/E2E behavior.

**Architecture:** `SystemNeurons` (the Aspire orchestrator grain) does *only* launch status + wiring. Surfaces for marketplace lists/trees, shell, charts, kit demos, etc. are emitted by their owning neurons (`MarketplaceNeuron`, `DataVisualizationNeuron`, etc.) via existing `FireAsync` + `HomeFeedBus` / `UiSurfaceRfwBridge` paths (already partially in use). Delete the inline duplication and direct `MarketplaceSeeds` usage for startup.

**Tech Stack:** .NET (net11.0), Orleans 10.2, xUnit/Reqnroll tests.

## Global Constraints

- Delete more than we add (Musk step 2 before simplify). No new god classes or large abstractions.
- Behavior preserving for clients (Flutter RFW default view, E2E, Telegram flows) and tests. If a surface disappears from default, it must be because its owning neuron now emits it (or it was pure demo bloat).
- Self-explanatory naming; no vacuous `/// <summary>` comments. Small inline comments only for genuinely non-obvious "why".
- Relative paths only; never `C:\Users\`.
- Run commands from `brain/`.
- Verification ritual after *every* change (and per task): `dotnet build` → `dotnet test --filter "<targeted e.g. Ui|Startup|NeuronCore|Marketplace>"` (high-severity relevant, per repo convention). `aspire doctor` / MCP tools (`list_resources`, etc.) only if AppHost/hosting files are touched (not expected here).
- Context7 (search + resolve + query for docs/examples) for **any** Orleans grain/journal/stream, gRPC streaming, Aspire resource, or serialization API before writing or editing code against it. Use latest package patterns from `Directory.Packages.props`.
- Never edit files under `.superpowers/sdd/`.
- Do not start other deferred slices (UnitTest1 split, gateway dispatch, etc.) in this plan.
- This plan is independent and shippable alone.

---

### Task 1: Audit exact current startup surface emissions (read + run)

**Files:**
- Read: `DigitalBrain.Kernel/SystemNeurons.cs` (Handle Start + related), `DigitalBrain.Core/MarketplaceSeeds.cs`, `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs` (if relevant), related tests.
- Test: `DigitalBrain.Tests/Ui/*`, `DigitalBrain.Tests/Kernel/*`, E2E filters if gated.

- [ ] **Step 1.1** Run targeted tests to capture current emitted surfaces at startup:
  `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Ui|FullyQualifiedName~NeuronCore|FullyQualifiedName~Marketplace" --logger "console;verbosity=detailed" 2>&1 | Select -Last 30`

- [ ] **Step 1.2** Direct read of emission sites (use offset reads for precision):
  Read `SystemNeurons.cs` sections around dashboard, market list/tree, shell nav, SE hello, tasks, chrome.
  Grep for `MarketplaceSeeds`, `UiSurface`, `BuildShellMenuItems`, hardcoded nav tuples in the file.

- [ ] **Step 1.3** Document the minimal set of surfaces that must remain observable for dev loop + tests (e.g. marketplace list, shell chrome, at least one chart/kit demo if used by E2E). Note which already have natural owners (MarketplaceNeuron etc.).

- [ ] **Step 1.4** Commit (or note in plan) the audit findings. No code change yet.

---

### Task 2: Strip bloat and direct seed/hardcode usage from SystemNeurons

**Files:**
- Modify: `DigitalBrain.Kernel/SystemNeurons.cs` (primary; delete large blocks of demo emission).
- Possibly light touch: `DigitalBrain.Core/MarketplaceSeeds.cs` (if any startup-only paths can be removed or made internal).

**Interfaces:**
- Consumes existing `HomeFeedBus`, `UiSurfaceRfwBridge`, `MarketplaceNeuron` etc. (no new).
- Produces: fewer lines in the grain; surfaces come from owners via typed `FireAsync<UiSurface>` or `Signal`.

- [ ] **Step 2.1** (Context7 first if touching any stream/bus) — if editing bus usage or grain activation: use Context7 MCP to resolve "Orleans streaming" / "Aspire gRPC" / relevant before edit. (Run `search_tool` for context7 if API details needed.)

- [ ] **Step 2.2** Delete the massive literal emission blocks in `HandleAsync(StartDistributedApp)` for dashboard details, direct seed market list/tree, hardcoded nav/shell trees, SE hello, demo tasks, etc. Leave only:
  - `DistributedAppStarted`
  - `SystemStatusChanged`
  - Minimal core shell if no owner yet (or delegate to a one-line emission from a thin owner).
  - Ensure any remaining core surface still fires via existing bus or owning path.

- [ ] **Step 2.3** Remove the local `static IEnumerable<...> BuildShellMenuItems()` (now dupe after prior delete of the helpers file).

- [ ] **Step 2.4** Remove or comment any direct `MarketplaceSeeds.LocalUiPacks` usage at startup (owning publish paths already emit).

- [ ] **Step 2.5** Build + targeted test (Ui + Marketplace + core):
  `dotnet build Brain.slnx`
  `dotnet test ... --filter "..."`

- [ ] **Step 2.6** Commit with message "cleanup(kernel): delete startup bloat + hardcoded demo surfaces from AspireOrchestrator (ownership to neurons via synapses)".

---

### Task 3: Ensure owning neurons emit required surfaces (or confirm they already do)

**Files:**
- Read/verify: `DigitalBrain.Kernel/MarketplaceNeuron.cs`, `DataVisualizationNeuron.cs`, `ChatNeuron.cs` (or equivalent), any Ui kit related.
- Test files that assert surfaces.

- [ ] **Step 3.1** For marketplace list/tree: confirm `MarketplaceNeuron` (on publish/filter or install) already fires the surfaces via bus (per prior work). If a gap, add minimal `FireAsync` of the live data surface in the right handler (typed).

- [ ] **Step 3.2** For chart/insights/demo kit: confirm `DataVisualizationNeuron` or pack paths emit. Delete pure demo-only if not required for proof.

- [ ] **Step 3.3** For shell/chrome: if needed, ensure a thin emission (or keep minimal in orchestrator only as last resort — prefer delete).

- [ ] **Step 3.4** Build + targeted tests. If any test expects exact demo content that we intentionally dropped, update to "contains core marketplace" style (self-explanatory).

- [ ] **Step 3.5** Commit.

---

### Task 4: Final verification + docs touch (if any)

**Files:** tests, possibly light `SYSTEM_DESIGN.md` or `CONTINUITY.md` (ownership clarification).

- [ ] **Step 4.1** Full relevant test run (not blanket unless the plan's purpose requires it):
  `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Ui|Marketplace|NeuronCore|Gateway" `
  (plus any E2E that can run without full aspire if gated).

- [ ] **Step 4.2** If no hosting touched: skip aspire. Else use MCP tools for doctor + list_resources on flutter-ui or kernel.

- [ ] **Step 4.3** Grep to confirm no new string dispatch or bloat introduced; no references to deleted inline dupe.

- [ ] **Step 4.4** Update `docs/SYSTEM_DESIGN.md` (if the startup emission ownership model changed materially) or `CONTINUITY.md` with one-line note. Keep minimal.

- [ ] **Step 4.5** Build + full targeted filter pass. 0 new failures.

- [ ] **Step 4.6** Commit + prepare for whole-branch review.

---

### Task 5: Whole-branch review + handoff

- [ ] Launch reviewer subagent (fresh) against the branch diff for the plan scope.
- [ ] Address any Critical/Important findings.
- [ ] Run final verification ritual (build + targeted tests).
- [ ] Hand off via finishing-a-development-branch skill / process (or note ready for PR).

**Self-review notes for plan author:**
- Spec coverage: directly implements the bloat delete + ownership move from the design doc and brainstorming output.
- Delete bias: primary activity is removal of hardcoded blocks.
- Placeholder-free: every step has concrete file or command.
- Sequencing respects: after NeuronTestBase (confidence), before higher-risk gateway.
- Guardrails followed in plan text.

This plan ships independently. After merge, next candidate (e.g. gateway dispatch hygiene or UnitTest1 split) can start on its own spec/ branch.
