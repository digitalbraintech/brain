# Fix Pre-Existing Test Failures — Design

**Date:** 2026-07-02
**Status:** Approved, ready for planning
**Scope:** `brain/` only — `DigitalBrain.Core`, `DigitalBrain.Kernel`, `DigitalBrain.Tests`, `samples/Awesome/SoftwareEngineering/`

## Context

This is a sub-project of the ongoing cleanup/refactoring/simplification initiative (see
`docs/plans/2026-07-01-cleanup-refactoring-simplification-30-steps-neuron-synapse-proof-plan.md` and
`docs/SYSTEM_DESIGN.md`). The prior sub-project (NeuronTestBase extraction) finished and flagged, but did not
fix, 4 pre-existing/unrelated test failures as follow-up debt (`.superpowers/sdd/progress.md`, Task 10/11
entries): `JournalJsonContextTests.ContextCoversEverySynapseSubtype`, `RollingUpdateRollbackTests`, and 2
Reqnroll scenarios in the "Software10" feature. This design root-causes and fixes all of them.

A fresh survey (this session) also identified 3 other candidate cleanup slices — collapsing duplicated
gateway routing tables, deleting dead Flutter screens, and splitting `DigitalBrain.Tests/UnitTest1.cs` — but
the user picked this test-failure fix as the first slice to tackle. Those remain candidates for future
sessions.

## Root cause 1: duplicate `PerformKernelSelfUpdate` record

`DigitalBrain.Kernel/SystemNeurons.cs:18` declares:

```csharp
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);
```

directly in `namespace DigitalBrain.Kernel`. This is structurally identical, field-for-field, to the canonical
record at `DigitalBrain.Core/Synapse.cs:473`. Because it is declared in the same namespace as every Kernel
file (rather than pulled in via `using`), C# resolves any unqualified `PerformKernelSelfUpdate` inside
`DigitalBrain.Kernel` to this local shadow in preference over the `using DigitalBrain.Core;`-imported
canonical type. `AspireOrchestratorNeuron : IHandle<PerformKernelSelfUpdate>` (`SystemNeurons.cs:21`) therefore
binds to the Kernel-local shadow type.

`Neuron.TryHandleViaDeclaredInterfaceAsync` (`DigitalBrain.Kernel/Neuron.cs:274-297`) dispatches by exact CLR
`Type` match (line 283: `if (handledType != synapse.GetType() && !handledType.IsAssignableFrom(synapse.GetType())) continue;`).
Any code that fires the type as `DigitalBrain.Core.PerformKernelSelfUpdate` — as
`DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs:19` and `DigitalBrain.Tests/Steps/NeuronSteps.cs:185`
both explicitly do — never matches `AspireOrchestratorNeuron`'s handler, which is silently never invoked. This
directly causes `RollingUpdateRollbackTests` to fail: the timeline stays empty of the `RollingDrain`/
`RollingRollback` `UiSurface` entries the real handler body would otherwise emit.

A second, corroborating data point: `NeuronCore.feature`'s "Kernel self-update" scenario fires the same
Core-qualified type at `NeuronSteps.cs:185` and yet passes today — only because its step definition
(`NeuronSteps.cs:186-219`) manually re-fires every expected `UiSurface`/`RestartResource` synapse itself,
with the comment "Pack-driven: fire the command (exercises handler) + emit surfaces using consts for reliable
assertion." This is a prior, undocumented workaround for the same bug, not evidence against it. This design
does not touch that workaround — cleaning it up once the underlying bug is fixed is test-quality-audit
territory (already planned as a separate future slice, see `docs/specs/2026-07-02-neuron-test-harness-consolidation-design.md`
§4), out of scope here.

Separately, `DigitalBrain.Kernel/JournalJsonContext.cs:131` has
`// [JsonSerializable(typeof(PerformKernelSelfUpdate))] // TODO: restore after cleanup` commented out. This is
what directly fails `JournalJsonContextTests.ContextCoversEverySynapseSubtype`, which scans
`DigitalBrain.Core`'s assembly for every `Synapse` subtype and asserts each has a matching
`[JsonSerializable]` registration.

**Blast radius today:** none in production. `MarketplaceNeuron.cs:138`'s bare `new PerformKernelSelfUpdate(cmd.Version)`
also resolves to the Kernel-local shadow (same-namespace rule), so the real self-update-via-marketplace-install
path happens to have both sides agreeing on the same (wrong) type today. The risk is latent: any future caller
firing this synapse from a namespace without the shadow in scope would silently go nowhere.

## Root cause 2: `ISoftware10Team` was never implemented

`DigitalBrain.Core/Synapse.cs:201` declares `public interface ISoftware10Team : ISoftwareEngineeringTeam { }`.
No grain class implements it anywhere in the solution (confirmed by full-repo grep). This is not incidental —
`samples/Awesome/SoftwareEngineering/readme.md` documents the intended pair: "Neurons: Software10TeamNeuron,
Software20TeamNeuron" contrasting rigid legacy codegen (Software10) against LLM-assisted codegen (Software20).
`Software20TeamNeuron` (`DigitalBrain.Kernel/Software20TeamNeuron.cs`, 31 lines) exists and works; its Software10
counterpart, along with a template file (`samples/Awesome/SoftwareEngineering/Software10/LegacyTodoApp.cs`),
was never built. The 2 failing Reqnroll scenarios in `DigitalBrain.Tests/Features/AwesomeSoftware10.feature`
both fail identically: `Reqnroll.BindingException: Could not find an implementation for interface
DigitalBrain.Core.ISoftware10Team`, raised from `Orleans.GrainInterfaceTypeToGrainTypeResolver.GetGrainType`
via `NeuronSteps.GivenASoftware10TeamNeuron` (`NeuronSteps.cs:75`).

**Decision (user, this session):** delete the unimplemented half of the demo rather than build it. Musk step 1
("question the requirement") applies directly — a two-team rigid-vs-LLM comparison demo is not load-bearing
for the platform, and the unbuilt half has sat unimplemented with zero product impact. Step 2 ("delete") wins
over building ~30 lines of new demo code that exists only to make a sample look symmetrical.

## Design

### Fix 1 — remove the duplicate type

- Delete the record declaration at `DigitalBrain.Kernel/SystemNeurons.cs:18`.
- Uncomment `[JsonSerializable(typeof(PerformKernelSelfUpdate))]` at `DigitalBrain.Kernel/JournalJsonContext.cs:131`
  (remove the stale `// TODO: restore after cleanup` comment along with it).
- No other code changes: every remaining unqualified `PerformKernelSelfUpdate` reference in
  `DigitalBrain.Kernel` (the `IHandle<PerformKernelSelfUpdate>` declaration, `MarketplaceNeuron.cs:138`) now
  resolves to `DigitalBrain.Core.PerformKernelSelfUpdate` via the existing `using DigitalBrain.Core;` — same
  field shape, so this is a behavior-preserving deletion, not a rewrite.

### Fix 2 — delete the unimplemented Software10 demo

- Delete `ISoftware10Team` (`DigitalBrain.Core/Synapse.cs:201`).
- Delete `DigitalBrain.Tests/Features/AwesomeSoftware10.feature` and its generated
  `DigitalBrain.Tests/Features/AwesomeSoftware10.feature.cs`.
- Delete the `GivenASoftware10TeamNeuron` step binding (`DigitalBrain.Tests/Steps/NeuronSteps.cs:72-77`). The
  shared `WhenISendCreateSimpleAppRequest`/`SimpleAppCreated` step bindings stay — they remain in use by the
  Software20 feature.
- Delete `samples/Awesome/SoftwareEngineering/Software10/` (its own `AwesomeSoftware10.feature` copy,
  `LegacyTodoApp.cs`, `readme.md`).
- Update `samples/Awesome/SoftwareEngineering/readme.md` to remove the Software10 mention, describing the
  sample as the Software20/LLM-assisted demo only.
- Update `docs/SYSTEM_DESIGN.md:428` — change "only **6 total**" to "only **5 total**" and drop
  `AwesomeSoftware10.feature` from the enumerated list.

### Non-goals

- Not touching `NeuronCore.feature`'s self-update workaround step (`NeuronSteps.cs:186-219`) — it still passes
  after Fix 1 and cleaning it up belongs to the already-planned test-quality audit, not this slice.
- Not touching the other 3 candidate slices identified this session (gateway dispatch duplication, dead
  Flutter screens, `UnitTest1.cs` split) — separate future slices.
- Not editing `.superpowers/sdd/*.md` session ledgers — they are historical records of past sessions and
  should read as accurate for the time they were written.

## Verification ritual

1. `dotnet build` (full solution) — 0 errors, and confirm zero remaining references to `ISoftware10Team`/
   `Software10` anywhere (`grep -rn "Software10" --include=*.cs`).
2. Targeted: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~JournalJsonContextTests|FullyQualifiedName~RollingUpdateRollbackTests"` — both pass.
3. Targeted: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~NeuronCoreFeature"` — still 11/11 passing (confirms the workaround-based scenario is unaffected).
4. Full `dotnet test` across every project in `Brain.slnx` — this slice's entire purpose is restoring a fully
   green suite, so (per the repo's own convention of a broader run once a slice completes) a full run is
   warranted here, not just a targeted one.
5. No `AppHost`/hosting files are touched, so `aspire doctor` is not part of this ritual.

## Risks

- Low. Fix 1 is a pure deletion of a byte-identical duplicate type plus restoring one commented-out
  attribute — no currently-passing path changes behavior. Fix 2 removes an interface with zero implementations
  and the tests/samples that reference it — nothing currently depends on `ISoftware10Team` existing.
