# Test Boilerplate Deduplication Implementation Plan

> Follow strictly with subagent-driven-development. Checkbox steps.

**Goal:** Remove duplicated manual cluster boilerplate from `DigitalBrain.Tests/` by migrating to `NeuronTestBase`. Split/extract contents of the grab-bag `UnitTest1.cs` (NeuronTests) into focused domain files. Delete duplication.

## Global Constraints
- Mechanical, behavior-preserving.
- Use existing `NeuronTestBase` (from TestKit).
- Relative paths only.
- Self-explanatory names; minimal comments.
- Verification: `dotnet build` + `dotnet test --filter "..."` (targeted high-sev) after changes.
- Branch: spec/test-boilerplate-deduplication (or current).
- No edits to .superpowers/sdd.
- Delete bias.

### Task 1: Inventory and audit remaining manual boilerplate

- [ ] Grep + reads (limits) for `TestClusterBuilder|NeuronTestSiloConfigurator|IAsyncLifetime.*_cluster` in `DigitalBrain.Tests/**/*.cs` (and Steps).
- [ ] List files still doing manual setup (UnitTest1.cs + CompanyKnowledgeTests, ContextRecallTests, UserSessionNeuronTests, Awesome/..., Mcp/..., Gateway/..., Kernel/..., Ui/..., etc.).
- [ ] Run baseline targeted tests + build.
- [ ] Note any strict configurator helpers or side-effect tests (git/temp).

### Task 2: Migrate non-grab-bag files to NeuronTestBase

- [ ] For each listed file: read full, change to inherit `NeuronTestBase`, replace manual cluster code with `Grain<T>`, `FireAsync`, `DeliverAsync`.
- [ ] Remove `InitializeAsync`/`DisposeAsync` boilerplate.
- [ ] Context7 if touching any grain patterns (unlikely for migration).
- [ ] After each (or groups): build + targeted filter for that area.
- [ ] Commit groups.

### Task 3: Split/extract UnitTest1.cs (NeuronTests) grab-bag

- [ ] Read full `UnitTest1.cs`.
- [ ] Create/extract focused files:
  - Kernel/NeuronCoreTests.cs (activation, journal, fire, status, checkpoint, tasks).
  - Distribution/MarketplaceTests.cs (publish, install, commission, private, sigs, embodiment).
  - Ui/ChartAndInsightsTests.cs + related.
  - Sdk/GitNeuronTests.cs (if not dupe with Developer.Tests).
  - Others as needed (Ino smoke, etc.).
- [ ] Delete or empty original UnitTest1.cs after moves (preserve useful comments).
- [ ] Update namespaces/classes as needed for clarity.
- [ ] Ritual after: build + broad relevant filter.

### Task 4: Cleanup + final verification

- [ ] Remove unused usings, any remaining dupe helpers.
- [ ] Grep for stray manual cluster code in the monolith.
- [ ] Full relevant test filters (Ui|NeuronCore|Marketplace + all touched) + build.
- [ ] Minimal CONTINUITY note if material.
- [ ] Prepare for reviewer (diff range).

### Task 5: Review + handoff

- [ ] Fresh reviewer subagent on diff vs plan.
- [ ] Address findings.
- [ ] Final ritual.
- [ ] Handoff/finishing.

This eliminates the post-NeuronTestBase dupe and makes the main test project consistent with the dedicated ones. Small, focused, delete-heavy where possible.