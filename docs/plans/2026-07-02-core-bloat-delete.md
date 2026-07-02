# Core Bloat Deletion Implementation Plan

> Use subagent-driven-development for tasks. Checkbox steps. Delete bias.

**Goal:** Remove demo bloat, hardcoded samples, and orphaned marker interfaces from DigitalBrain.Core so it is closer to the "pure protocol" it claims to be (Synapse + INeuron + IHandle foundation only).

## Global Constraints
- Delete > add.
- Relative paths.
- Targeted `dotnet test --filter` after changes.
- Self-explanatory names, no vacuous comments.
- Context7 for any API if touched (unlikely).
- Branch: spec/core-bloat-delete.
- Do not touch completed prior work.

### Task 1: Delete small self-contained dead files and orphaned interfaces

- [ ] Delete `DigitalBrain.Core/Awesome/ReviewSynapses.cs`, `CodeFoundrySynapses.cs`, `CompanySkillSynapses.cs`, `Ui/RfwCard.cs`, `AssemblyInfo.cs` (if safe).
- [ ] In `Synapse.cs`, delete the list of orphaned interfaces (IAspire, IMarketplace, ICompiler, IChannelNeuron, ISoftware10Team, ISoftware20Team, IInoNeuron, ISystemStatus, IDemoNeuron, and the other ~15 that have no impls in Core).
- [ ] Build + targeted test (Protocol, Marketplace, Ui).
- [ ] Commit.

### Task 2: Strip demo pack code literals from MarketplaceSeeds.cs

- [ ] Keep only the essential (TelegramResponder if canonical, PersonalAssistant embedded path, minimal consts).
- [ ] Delete the long """ HelloWorldPackCode, SimpleColorPickerPackCode, and other demo literals + their Hops consts.
- [ ] Remove unused `LocalUiPacks` entries that were only for demos if safe.
- [ ] Build + test.
- [ ] Commit.

### Task 3: Strip hardcoded sample builders from UiSurfaces.cs

- [ ] Delete `UiSurfaceSamples.*` methods, `BuildWorkbenchSurfaces`, all `FromTimeline*`, `TaskManagerFromTasks`, `InstalledBundlesFromPacks`, `BuildInstalledLauncherTree`, `ChartSurfacesFromTimeline`, and the pack-specific experience row logic.
- [ ] Keep only core `UiSurface`, `ForRfw`, `ForExperienceHop`, `ForWidgetTree`, and minimal live data helpers if used by real paths.
- [ ] Verify no break in real flows (Marketplace, tasks, etc. now come from owners).
- [ ] Build + targeted tests.
- [ ] Commit.

### Task 4: Final verification

- [ ] Full relevant test filter across Core-touching projects.
- [ ] Grep for any remaining references to deleted items inside Core.
- [ ] Update SYSTEM_DESIGN.md line counts if material.
- [ ] Final build + test pass.
- [ ] Reviewer subagent + finishing.

This slice is pure deletion, small scope, high impact for "make requirements less dumb + delete".