# Fix Pre-Existing Test Failures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore a fully green `brain/` test suite by fixing 3 pre-existing, previously-flagged-but-unfixed
test failures: `JournalJsonContextTests.ContextCoversEverySynapseSubtype`, `RollingUpdateRollbackTests`, and 2
Reqnroll scenarios in `AwesomeSoftware10.feature`.

**Architecture:** Two independent, unrelated root causes, each fixed by deletion rather than new code. Fix 1
deletes a duplicate `PerformKernelSelfUpdate` record that shadows the canonical `DigitalBrain.Core` type and
silently breaks reflective `IHandle<T>` dispatch. Fix 2 deletes an interface (`ISoftware10Team`) and its
feature/sample scaffolding that were never backed by a grain implementation.

**Tech Stack:** .NET (net11.0), Orleans 10.2, xUnit, Reqnroll (BDD `.feature` files), `System.Text.Json`
source-generated serialization context.

## Global Constraints

- Every fix in this plan is root-caused already (see `docs/specs/2026-07-02-fix-pre-existing-test-failures-design.md`)
  — no new investigation is expected mid-task. If a step's expected result doesn't match reality, stop and
  re-diagnose rather than pushing through.
- Do not touch `NeuronCore.feature`'s self-update workaround step body (`DigitalBrain.Tests/Steps/NeuronSteps.cs:186-219`)
  beyond what's incidentally required by Fix 1 (nothing is required — it must keep passing unchanged).
- Do not edit any file under `.superpowers/sdd/` — those are historical session ledgers, accurate for the time
  they were written.
- Do not start any of the other 3 candidate slices identified in the design doc (gateway dispatch duplication,
  dead Flutter screens, `UnitTest1.cs` split) — separate future work.
- Relative paths only; never reference `C:\Users\` paths.
- Self-explanatory naming; no vacuous `/// <summary>` comments.
- Run commands from the `brain/` directory (the repo root for this work), on branch `spec/fix-pre-existing-test-failures`.
- Verification per task: `dotnet build` (implicit in `dotnet test`) → targeted `dotnet test --filter "..."` —
  matching this repo's own stated convention of targeted runs during the inner loop. The final task runs the
  full suite, since restoring a fully green suite is this plan's entire purpose.

---

### Task 1: Remove the duplicate `PerformKernelSelfUpdate` record

**Files:**
- Modify: `DigitalBrain.Kernel/SystemNeurons.cs:17-18`
- Modify: `DigitalBrain.Kernel/JournalJsonContext.cs:131`
- Test: `DigitalBrain.Tests/Protocol/JournalJsonContextTests.cs` (existing, unmodified), `DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs` (existing, unmodified)

**Interfaces:**
- Consumes: `DigitalBrain.Core.PerformKernelSelfUpdate` (`DigitalBrain.Core/Synapse.cs:473`, unchanged by this
  task) — already in scope in every `DigitalBrain.Kernel` file via `using DigitalBrain.Core;`.
- Produces: nothing new. After this task, every unqualified `PerformKernelSelfUpdate` reference inside
  `DigitalBrain.Kernel` (the `IHandle<PerformKernelSelfUpdate>` declaration on `AspireOrchestratorNeuron`,
  and `MarketplaceNeuron.cs:138`'s `new PerformKernelSelfUpdate(cmd.Version)`) resolves to
  `DigitalBrain.Core.PerformKernelSelfUpdate` — same field shape, so no other call site changes.

- [ ] **Step 1: Delete the duplicate record declaration**

In `DigitalBrain.Kernel/SystemNeurons.cs`, the current content around lines 10-21 reads:

```csharp
public static class KernelPack
{
    public const string Name = "kernel";
    public const string DefaultVersion = "0.3.0";
    public const string Description = "Core kernel substrate. Pre-installed; updatable via marketplace with rolling replica support.";
}

[GenerateSerializer]
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);

[GrainType("digitalbrain.kernel.aspire.v1")]
public class AspireOrchestratorNeuron(ILogger<AspireOrchestratorNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IAspireNeuron, IHandle<PerformKernelSelfUpdate>
```

Delete the `[GenerateSerializer]` attribute line and the `PerformKernelSelfUpdate` record line (the two lines
between `KernelPack`'s closing brace and the `[GrainType(...)]` attribute), so it reads:

```csharp
public static class KernelPack
{
    public const string Name = "kernel";
    public const string DefaultVersion = "0.3.0";
    public const string Description = "Core kernel substrate. Pre-installed; updatable via marketplace with rolling replica support.";
}

[GrainType("digitalbrain.kernel.aspire.v1")]
public class AspireOrchestratorNeuron(ILogger<AspireOrchestratorNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IAspireNeuron, IHandle<PerformKernelSelfUpdate>
```

- [ ] **Step 2: Restore the `JournalJsonContext` registration**

In `DigitalBrain.Kernel/JournalJsonContext.cs`, find:

```csharp
[JsonSerializable(typeof(RfwCard))]
// [JsonSerializable(typeof(PerformKernelSelfUpdate))] // TODO: restore after cleanup
// Experience domain
```

Replace the commented-out line with an active registration:

```csharp
[JsonSerializable(typeof(RfwCard))]
[JsonSerializable(typeof(PerformKernelSelfUpdate))]
// Experience domain
```

- [ ] **Step 3: Build**

Run: `dotnet build Brain.slnx`
Expected: 0 errors. (This confirms no other file had an unqualified `PerformKernelSelfUpdate` reference that
depended on the deleted Kernel-local type in an incompatible way — the type is structurally identical to
Core's, so none should exist, but the build is the check.)

- [ ] **Step 4: Run the two targeted failing tests to confirm they now pass**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~JournalJsonContextTests|FullyQualifiedName~RollingUpdateRollbackTests"`
Expected: PASS — 2 tests, 0 failures (previously: `JournalJsonContextTests.ContextCoversEverySynapseSubtype`
failed with "JournalJsonContext missing: PerformKernelSelfUpdate"; `RollingUpdateRollbackTests.Verify_Failure_Rolls_Back_And_Does_Not_Complete`
failed with an empty timeline).

- [ ] **Step 5: Confirm the pre-existing workaround scenario is unaffected**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~NeuronCoreFeature"`
Expected: PASS — 11 tests, 0 failures (same count as before this task; `NeuronCoreFeature`'s self-update
scenario manually re-fires its own `UiSurface` synapses per `NeuronSteps.cs:186-219`, unmodified by this task,
so it was already passing and must remain passing).

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Kernel/SystemNeurons.cs DigitalBrain.Kernel/JournalJsonContext.cs
git commit -m "fix(kernel): remove duplicate PerformKernelSelfUpdate record shadowing Core's canonical type"
```

---

### Task 2: Delete the unimplemented Software10 demo

**Files:**
- Modify: `DigitalBrain.Core/Synapse.cs:201` (delete `ISoftware10Team`)
- Delete: `DigitalBrain.Tests/Features/AwesomeSoftware10.feature`
- Delete: `DigitalBrain.Tests/Features/AwesomeSoftware10.feature.cs`
- Modify: `DigitalBrain.Tests/Steps/NeuronSteps.cs:72-77` (delete `GivenASoftware10TeamNeuron`)
- Delete: `samples/Awesome/SoftwareEngineering/Software10/` (directory: `AwesomeSoftware10.feature`,
  `LegacyTodoApp.cs`, `readme.md`)
- Modify: `samples/Awesome/SoftwareEngineering/readme.md`
- Modify: `docs/SYSTEM_DESIGN.md:428`
- Test: `DigitalBrain.Tests/Features/AwesomeSoftware20.feature` (existing, unmodified — must keep passing)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. This task only deletes an interface with zero implementations and its associated
  test/sample scaffolding; `ISoftwareEngineeringTeam` and `ISoftware20Team` (`DigitalBrain.Core/Synapse.cs:196,199`)
  are untouched.

- [ ] **Step 1: Delete `ISoftware10Team`**

In `DigitalBrain.Core/Synapse.cs`, find:

```csharp
public interface ISoftwareEngineeringTeam : INeuron, IHandle<CreateSimpleApp> { }

[LLM<Qwen>]
public interface ISoftware20Team : ISoftwareEngineeringTeam { }

public interface ISoftware10Team : ISoftwareEngineeringTeam { }

public interface IInoNeuron : INeuron, IHandle<InoRequest>
```

Delete the `ISoftware10Team` line and the blank line above it, so it reads:

```csharp
public interface ISoftwareEngineeringTeam : INeuron, IHandle<CreateSimpleApp> { }

[LLM<Qwen>]
public interface ISoftware20Team : ISoftwareEngineeringTeam { }

public interface IInoNeuron : INeuron, IHandle<InoRequest>
```

- [ ] **Step 2: Delete the Software10 feature files and sample directory**

```bash
git rm DigitalBrain.Tests/Features/AwesomeSoftware10.feature DigitalBrain.Tests/Features/AwesomeSoftware10.feature.cs
git rm -r samples/Awesome/SoftwareEngineering/Software10
```

- [ ] **Step 3: Delete the `GivenASoftware10TeamNeuron` step binding**

In `DigitalBrain.Tests/Steps/NeuronSteps.cs`, find:

```csharp
    [Given(@"a software10 team neuron ""(.*)""")]
    public async Task GivenASoftware10TeamNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ISoftware10Team>(id);
        await _currentGrain.GetTimelineAsync();
    }

    [Given(@"a software20 team neuron ""(.*)""")]
    public async Task GivenASoftware20TeamNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ISoftware20Team>(id);
        await _currentGrain.GetTimelineAsync();
    }
```

Delete the `GivenASoftware10TeamNeuron` method and its attribute (keep `GivenASoftware20TeamNeuron` — it's
still used by the Software20 feature), so it reads:

```csharp
    [Given(@"a software20 team neuron ""(.*)""")]
    public async Task GivenASoftware20TeamNeuron(string id)
    {
        _currentGrain = _cluster.GrainFactory.GetGrain<ISoftware20Team>(id);
        await _currentGrain.GetTimelineAsync();
    }
```

- [ ] **Step 4: Update the sample readme**

Replace the full contents of `samples/Awesome/SoftwareEngineering/readme.md` with:

```markdown
# Awesome Software Engineering

Demonstrates the NeuroOS neuron system (with local LLM support via Qwen) creating a simple app.

## Team

- **Software20**: neuro-aware, prefers local LLM for higher quality generation. See Software20/

## Test files
The executable spec is:
- DigitalBrain.Tests/Features/AwesomeSoftware20.feature

Exposes:
- Synapses: CreateSimpleApp, SimpleAppCreated
- Neurons: Software20TeamNeuron (tagged with [LLM<Qwen>])

Run `dotnet test --filter Awesome` to verify the team can create simple apps.

When running full `aspire run` (with Ollama + qwen model downloaded), Software20 will use the real local LLM for app code.
```

- [ ] **Step 5: Update `SYSTEM_DESIGN.md`'s feature file count**

In `docs/SYSTEM_DESIGN.md`, find:

```markdown
`.feature` files are flat under `Features/` — only **6 total**: `AwesomeSoftware10.feature`,
`AwesomeSoftware20.feature`, `CodeFoundry.feature`, `NeuronCore.feature`,
`MarketplaceUserFlows.feature`, `TelegramExperience.feature`. Concern-based organization instead lives
```

Replace with:

```markdown
`.feature` files are flat under `Features/` — only **5 total**: `AwesomeSoftware20.feature`,
`CodeFoundry.feature`, `NeuronCore.feature`, `MarketplaceUserFlows.feature`, `TelegramExperience.feature`.
Concern-based organization instead lives
```

- [ ] **Step 6: Build and confirm zero remaining references**

Run: `dotnet build Brain.slnx`
Expected: 0 errors.

Run: `grep -rn "Software10" --include=*.cs --include=*.feature .`
Expected: no output (excluding `.superpowers/sdd/*.md`, which this task does not touch and `grep --include=*.cs --include=*.feature` does not match anyway).

- [ ] **Step 7: Run the Awesome-tagged tests to confirm only Software20 remains and passes**

Run: `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Software20"`
Expected: PASS — 2 tests, 0 failures (`Software20TeamCreatesAModernSelf_DocumentingTaskApp`,
`Software20TeamCreatesASimpleChat_LikeExperience`).

- [ ] **Step 8: Commit**

```bash
git add DigitalBrain.Core/Synapse.cs DigitalBrain.Tests/Steps/NeuronSteps.cs samples/Awesome/SoftwareEngineering/readme.md docs/SYSTEM_DESIGN.md
git commit -m "test(awesome): delete unimplemented Software10 demo (ISoftware10Team had zero grain implementations)"
```

---

### Task 3: Final verification pass

**Files:** none (verification only)

**Interfaces:** none

- [ ] **Step 1: Full solution build**

Run: `dotnet build Brain.slnx`
Expected: Build succeeds, 0 errors.

- [ ] **Step 2: Full test run across every project**

Run: `dotnet test Brain.slnx`
Expected: 0 failures across all projects. This is the first full, unfiltered run in this plan — appropriate
here because restoring a fully green suite is this plan's entire purpose (per the repo's own convention of a
broader run once a slice is complete).

- [ ] **Step 3: If Step 2 reveals any failure not covered by Tasks 1-2**

Stop. Do not attempt a fix inline — this plan's diagnosis (`docs/specs/2026-07-02-fix-pre-existing-test-failures-design.md`)
covered exactly 3 known failures; any other failure is new information requiring its own root-cause
investigation (`superpowers:systematic-debugging`) before a fix is proposed. Report it rather than patching it.

---

## Self-Review Notes

- **Spec coverage:** This plan implements both fixes from `docs/specs/2026-07-02-fix-pre-existing-test-failures-design.md`
  in full — Fix 1 (Task 1), Fix 2 (Task 2, including the readme and `SYSTEM_DESIGN.md` updates called out in
  the spec's Design section), plus the spec's verification ritual (Task 3).
- **Placeholder scan:** No TBD/TODO/"add appropriate X"; every step shows complete before/after file content
  or exact shell commands.
- **Type consistency:** `PerformKernelSelfUpdate`, `ISoftware10Team`, `ISoftware20Team`, `GivenASoftware10TeamNeuron`,
  `GivenASoftware20TeamNeuron` are referenced identically to their current on-disk names throughout — no
  renaming occurs in this plan.
