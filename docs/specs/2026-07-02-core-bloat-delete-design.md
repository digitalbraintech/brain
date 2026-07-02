# Core Bloat and Orphaned Code Deletion — Design

**Date:** 2026-07-02
**Status:** Design, pending implementation plan
**Scope:** `brain/DigitalBrain.Core` only — focus on MarketplaceSeeds.cs, UiSurfaces.cs, Synapse.cs, and small self-contained synapse files. Pure delete.

## Context

This is a direct continuation of the cleanup/refactoring/simplification initiative (root plan `docs/plans/2026-07-01-cleanup-refactoring-simplification-30-steps-neuron-synapse-proof-plan.md`, `docs/SYSTEM_DESIGN.md`, Musk 5-step, 07-02 plans).

**Fresh research input (Core explore subagent, all facts verified by direct `read_file` + `grep` + `list_dir` on relative paths):**
- 31 .cs files.
- Largest bloat: `UiSurfaces.cs` (1585 lines — mostly `UiSurfaceSamples.*`, `Build*`, `FromTimeline*`, `TaskManager*`, `InstalledBundles*`, hardcoded demo experiences, pack-specific rows).
- `MarketplaceSeeds.cs` (437 lines — full """ multiline pack code literals for TelegramResponder, HelloWorld, SimpleColorPicker, ExcelViz, KeywordWatcher, Dummy, plus PersonalAssistant via embedded + many helper consts + `LocalUiPacks`).
- `Synapse.cs` (472 lines): ~20+ orphaned marker interfaces (`IAspire`, `IMarketplace`, `ICompiler`, `IChannelNeuron`, `ISoftware*Team`, many `I*Neuron` that have no impls in Core because grains moved to Kernel/ino projects per prior migration).
- Other self-contained dead: `CodeFoundrySynapses.cs`, `CompanySkillSynapses.cs`, `Awesome/ReviewSynapses.cs`, `Ui/RfwCard.cs` (only self-refs + deprecation note).
- `AssemblyInfo.cs` (just InternalsVisibleTo).
- `DigitalBrain.Core.csproj` has EmbeddedResource for PersonalAssistantNeuron.cs (build coupling).

Previous completed (do not redo): ino isolation, NeuronTestBase, fix-pre-existing (including Software10), SystemNeurons bloat delete (just shipped).

The Core is supposed to be the "pure protocol layer" (per SYSTEM_DESIGN and csproj). Instead it carries a lot of demo/harvested bloat and orphan markers.

## Goals

- Delete as much non-protocol bloat as possible (Musk delete-first).
- Remove orphaned interfaces and self-contained demo synapse files.
- Strip demo pack code literals and hardcoded sample builders from seeds and surfaces (keep only what is truly load-bearing for marketplace seeds or core protocol).
- Result: smaller, cleaner Core that is easier to reason about as the "INeuron + Synapse + IHandle" foundation.
- Keep behavior for anything that actually uses the real packs (Telegram, PersonalAssistant via embedded).

## Non-goals

- Changing how real packs work or the embedded mechanism.
- Touching Kernel, inos, tests (beyond any minimal reference cleanup if a delete breaks a using).
- App/ side.
- New features or refactors (pure delete).

## Design

**Delete candidates (file:line evidence from subagent + reads):**
- `MarketplaceSeeds.cs`: the long """ blocks for HelloWorldPackCode, SimpleColorPickerPackCode, and other demo ones (keep only TelegramResponder if still canonical, and the PersonalAssistant embedded path).
- `UiSurfaces.cs`: all the `UiSurfaceSamples`, `BuildWorkbenchSurfaces`, `ActivityGraphFromTimeline`, `Marketplace*FromPacks`, `TimelineFromSynapses`, `TaskManagerFromTasks`, `InstalledBundlesFromPacks`, `BuildInstalledLauncherTree`, `ChartSurfacesFromTimeline`, and pack-specific experience rows.
- `Synapse.cs`: the list of orphaned `I*` interfaces that have zero implementations in Core.
- Small files: entire `CodeFoundrySynapses.cs`, `CompanySkillSynapses.cs`, `Awesome/ReviewSynapses.cs`, `Ui/RfwCard.cs`.
- `AssemblyInfo.cs` if only test visibility.
- The EmbeddedResource for PersonalAssistant if it can be moved (but per prior, it's the single-source now; leave if load-bearing).

After deletes, run full `grep` for any remaining references inside Core (and fix minimal using if any).

Verification: `dotnet build` + targeted test on anything that touches Core (Protocol, Marketplace, etc.).

## Risks

Low. These are demo/harvested/orphaned items. Real functionality (Telegram responder, PersonalAssistant) is preserved via the embedded path. If a test or seed usage breaks, the plan will update the minimal necessary caller.

This is classic Musk step 2 after the recent Kernel delete slice.

## Suggested sequencing

1. Delete the small whole files + orphaned interfaces in Synapse.cs.
2. Strip the demo literals from MarketplaceSeeds.cs.
3. Strip the sample builders from UiSurfaces.cs.
4. Verify build + relevant tests + zero orphaned refs left.
5. Light docs update if needed (SYSTEM_DESIGN size claims).

This slice is small, focused, delete-heavy, and ships independently.