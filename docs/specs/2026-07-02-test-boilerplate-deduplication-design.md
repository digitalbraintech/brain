# Test Boilerplate Deduplication and UnitTest1 Cleanup — Design

**Date:** 2026-07-02
**Status:** Design, pending implementation plan
**Scope:** Primarily `brain/DigitalBrain.Tests/` (the monolith test project) + related Steps. Leverage existing `DigitalBrain.TestKit.NeuronTestBase`.

## Context

This continues the cleanup (root 30-step plan, prior specs/plans, Musk 5-step, subagent research).

**What's already done (no duplication):**
- Ino isolation complete.
- NeuronTestBase + migration of 14 files in dedicated *.Tests projects complete (base in TestKit).
- Fix pre-existing (duplicate Perform, Software10 demo) complete.
- SystemNeurons bloat delete complete (fresh implementer subagent followed plan: stripped ~246 lines demo emission; owners emit via buses; rituals green).
- Core demo bloat advanced (Dummy/excel-viz/HelloWorld/Simple/UiGallery literals + Hops + entries removed from MarketplaceSeeds; hello special cases from UiSurfaces; dead test file + temps + ref cleanups; builds/tests green).

**Latest research (Tests subagent + Core):**
- Main `DigitalBrain.Tests/` still has heavy manual boilerplate (IAsyncLifetime + TestClusterBuilder + NeuronTestSiloConfigurator) in UnitTest1.cs (grab-bag ~30 Facts, 600+ LOC, string .Type checks, buried Ino test) and many other files (Company, Context, Auth, Awesome, Mcp, Gateway, Kernel, Ui, Steps, etc.).
- Dedicated projects are clean (inherit base).
- UnitTest1 is the legacy catch-all.
- Good assertions but dupe setup hurts maintainability and "independently testable" vision.
- Thin coverage (Ino, DbSupport orphan).

This is the explicit next phase from the NeuronTestBase design ("grab-bag... split into 9 domain files").

## Goals

- Migrate remaining manual cluster setup in DigitalBrain.Tests/ to use NeuronTestBase (or TestDigitalBrain directly for non-grain).
- Extract/split UnitTest1.cs contents into focused files under existing folders (Kernel/, Distribution/, Ui/, etc.) or delete pure duplication.
- Eliminate dupe boilerplate (hundreds of lines).
- Keep behavior/assertions identical.
- Delete bias where possible.

## Non-goals

- Full rewrite of E2E/Reqnroll (focus on xUnit grain tests).
- Adding new coverage (that's separate).
- Touching Core/Kernel production code beyond minimal.

## Design

- Inventory remaining manual files (grep for TestClusterBuilder etc. in **/*.cs under tests).
- For each: change to : NeuronTestBase, replace _cluster.GrainFactory... with Grain<T>, FireAsync calls, remove Initialize/Dispose boilerplate.
- For UnitTest1: split by concern into e.g. Kernel/NeuronCoreTests.cs, Distribution/MarketplaceTests.cs, Ui/..., etc. (preserve comments where useful, self-explanatory names).
- Update any strict configurator helpers if needed (move to TestKit if reusable).
- Remove unused usings after.
- Verify with targeted tests + build.

Verification: build + targeted filters for affected (Ui|NeuronCore|Marketplace|...); full relevant at end.

## Risks

Low (mechanical lift-and-shift; tests already green; base proven on other projects).

High value for maintainability and the "independently testable" goal.

This is the logical next after the base extraction and the bloat deletes.