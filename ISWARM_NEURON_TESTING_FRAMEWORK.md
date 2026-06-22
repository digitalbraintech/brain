# ISwarm Neuron Testing Framework

## Purpose

Assess the next DigitalBrain software engineering step: an `ISwarm : INeuron`
system that can reason over a C# codebase, model each relevant file as a
software neuron, create focused `IGroupChat : INeuron` discussions, and drive
fast self-improving development through kernel tasks, simulations, checkpoints,
and branches.

This document is the first swarm-assigned task output: define the testing and
iteration framework that lets the system improve itself without turning every
change into a slow full-cluster run.

## Current Foundation

DigitalBrain already has most of the required kernel primitives:

- `INeuron` exposes `FireAsync`, `DeliverAsync`, timelines, dual journals,
  `CreateCheckpointAsync`, and `BranchAsync`.
- `Neuron` is an Orleans `DurableGrain` with incoming and outgoing journals.
- `IKernelTask` is a first-class journal-driven task primitive.
- `IInoNeuron` can create kernel tasks and branches from intent.
- `SystemStatusNeuron` already runs status diagnosis and simulation from
  checkpoints.
- `SoftwareEngineeringClosedLoopNeuron` already models runtime software
  improvement through LLM, Aspire MCP, marketplace, and restart/apply paths.
- Tests already use Orleans `TestCluster`, in-memory durable lists, xUnit, and
  Reqnroll feature specs.

The swarm should extend these primitives. It should not create a separate
runtime or a second agent framework.

## Assessment

The concept is viable, but the phrase "load each C# file like
`CSharpFile : Neuron`" needs a precise implementation boundary.

Do not keep 600 full file bodies active in 600 long-lived grain prompts. That
would be slow, expensive, and noisy. Instead, create lazy file neurons backed by
a Roslyn index:

- each C# file gets a stable neuron id derived from repository path and content
  hash;
- file neurons store compact facts, not full prompt state;
- source text is loaded only when a task selects that file;
- summaries and dependency facts are cacheable and invalidated by file hash;
- group chats include only the selected evidence set, normally 3 to 12 files
  plus role neurons.

This keeps the system intelligent because it reasons over structure, symbols,
dependencies, tests, and history. It keeps the system performant because it does
not ask every neuron to talk on every task.

## Proposed Neurons

`ISwarm : INeuron`

The software engineering coordinator. It receives a task, builds or refreshes a
codebase snapshot, selects relevant neurons, creates a group chat, turns the
decision into kernel tasks, and gates any modification through simulation and
tests.

`ICSharpFileNeuron : INeuron`

The file-level participant. It owns one C# file's path, hash, declarations,
referenced symbols, diagnostics, tests touching it, and compact summary. It can
answer focused questions about its file and produce patch proposals, but it does
not apply patches directly.

`IGroupChat : INeuron`

The temporary discussion neuron. It joins selected file neurons and role neurons
for a specific task. Its output must be structured: decision, touched files,
test plan, risk, and patch plan.

`IRoslynIndexNeuron : INeuron`

The index builder. It owns solution/project loading, symbol graph extraction,
dependency edges, diagnostics, and test discovery. This should use Roslyn APIs,
not regex over source files.

`ITestStrategyNeuron : INeuron`

The test selector. It maps a proposed change to the fastest sufficient
verification command and escalates only when risk requires it.

`IBranchSimulationNeuron : INeuron`

The isolated what-if executor. It creates checkpoints, replays relevant synapses,
drives proposed changes in an isolated branch/worktree or simulated journal, and
records the outcome as `SimulationResult`.

## Core Synapses

Suggested contracts for the MVP:

```csharp
public record SwarmTask(string TaskId, string Goal, string RepositoryRoot)
    : Synapse(nameof(SwarmTask), DateTimeOffset.UtcNow);

public record CodebaseSnapshot(string SnapshotId, string SolutionPath, int FileCount)
    : Synapse(nameof(CodebaseSnapshot), DateTimeOffset.UtcNow);

public record CSharpFileIndexed(
    string Path,
    string Hash,
    string[] DeclaredSymbols,
    string[] ReferencedSymbols,
    string Summary)
    : Synapse(nameof(CSharpFileIndexed), DateTimeOffset.UtcNow);

public record GroupChatRequested(string ChatId, string TaskId, string[] ParticipantIds)
    : Synapse(nameof(GroupChatRequested), DateTimeOffset.UtcNow);

public record GroupChatDecision(
    string ChatId,
    string Decision,
    string[] FilesToEdit,
    string[] TestsToRun,
    string Risk)
    : Synapse(nameof(GroupChatDecision), DateTimeOffset.UtcNow);

public record PatchPlan(string TaskId, string[] Files, string Rationale, string DiffSummary)
    : Synapse(nameof(PatchPlan), DateTimeOffset.UtcNow);

public record TestSelection(string TaskId, string[] Commands, string Reason)
    : Synapse(nameof(TestSelection), DateTimeOffset.UtcNow);
```

Keep these records small and serializable. Large diffs, source text, and logs
should be referenced by path/hash or stored as artifacts, not duplicated into
every journal entry.

## Fast Iteration Framework

The swarm needs layered validation. Each layer must be cheap enough that the
system can run it frequently.

### Layer 0: Pure Contract Tests

Scope: protocol records, selectors, path normalization, hash calculation,
participant selection, risk classification.

Target speed: under 1 second.

Command shape:

```sh
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Category=Contract"
```

These tests should not start Orleans, Aspire, Ollama, or MCP.

### Layer 1: In-Memory Neuron Tests

Scope: `ISwarm`, `ICSharpFileNeuron`, `IGroupChat`, `IKernelTask`, checkpoint,
branch, and journal behavior with Orleans `TestCluster`.

Target speed: 5 to 15 seconds.

Use the existing test support pattern: memory grain storage, memory streams,
in-memory durable lists, no real LLM dependency.

Test examples:

- firing `SwarmTask` creates a `CodebaseSnapshot`;
- snapshot indexes C# files and emits `CSharpFileIndexed`;
- task selection chooses a small participant set;
- `GroupChatDecision` includes files, tests, and risk;
- kernel task lifecycle reaches `KernelTaskCompleted`;
- branch activity does not pollute the source neuron's journal.

### Layer 2: Deterministic Simulations

Scope: what-if behavior without editing the real working tree.

Every self-improvement proposal should be able to run through:

1. create checkpoint;
2. create isolated branch id or temp worktree;
3. replay relevant synapses;
4. apply proposed patch to isolated state only;
5. run selected tests;
6. emit `SimulationResult`.

LLM calls must be stubbed or replayed from fixed fixtures at this layer. The
simulation should fail if it depends on nondeterministic chat output.

### Layer 3: Targeted Build And Test

Scope: real compilation plus the smallest meaningful test set.

Examples:

```sh
dotnet build DigitalBrain.Protocol/DigitalBrain.Protocol.csproj --no-restore
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --no-build --filter "FullyQualifiedName~NeuronTests"
```

Use this layer for real code edits. The `ITestStrategyNeuron` should pick the
commands from changed projects, referenced projects, touched feature specs, and
diagnostic risk.

### Layer 4: Aspire Distributed Validation

Scope: AppHost model, resource graph, MCP integration, Ollama-backed behavior,
multi-replica kernel behavior, and end-to-end demos.

This layer is intentional, not default. Use it when changes touch Aspire,
distributed resource wiring, full self-awareness, or LLM-backed closed loops.

The repo guidance already says the fast inner loop is the default and full
distributed validation is for major PRs or self-awareness/LLM flows.

## Swarm Execution Flow

1. User or INO fires `SwarmTask`.
2. `ISwarm` requests or refreshes `CodebaseSnapshot`.
3. `IRoslynIndexNeuron` indexes solution structure, C# files, symbols,
   references, diagnostics, and known tests.
4. `ISwarm` selects a small participant set:
   - files declaring touched symbols;
   - tests covering those symbols;
   - direct callers/callees;
   - one architect role;
   - one test strategy role.
5. `IGroupChat` discusses only the selected evidence pack.
6. `GroupChatDecision` produces a patch plan and test selection.
7. `ISwarm` creates kernel tasks:
   - index;
   - plan;
   - simulate;
   - patch;
   - test;
   - summarize.
8. `IBranchSimulationNeuron` creates a checkpoint or isolated branch and runs
   the what-if.
9. If tests pass, the patch can move to normal repository editing.
10. Journals store the decision, test evidence, and learning summary.

## Performance Rules

- Never prompt all file neurons for every task.
- Never store full source files repeatedly in journals.
- Never let group chat participants grow without a cap.
- Prefer Roslyn semantic facts over text search for C# understanding.
- Cache file summaries by content hash.
- Use deterministic, non-LLM selection first; use LLM only after candidate
  context is already narrowed.
- Persist summaries and decisions as compact synapses.
- Treat build/test output as artifacts with summarized journal entries.

## Safety Rules For Self-Improvement

- The swarm may propose patches, but direct mutation must go through kernel
  tasks and a test gate.
- A branch or simulation result is required before any self-improving change is
  accepted.
- Group chat output must cite concrete files and tests.
- A failed test run writes learning back to the task journal, not just to logs.
- Live Aspire resource restart is a separate action after local validation.
- Generated code enters through marketplace or explicit file edits, never
  hidden runtime mutation.

## MVP Build Plan

1. Add protocol-only contracts for `ISwarm`, `ICSharpFileNeuron`,
   `IGroupChat`, and the synapses listed above.
2. Add pure tests for path ids, hashing, and participant selection.
3. Add `RoslynIndexNeuron` that indexes `framework/NeuroOS.slnx` and emits
   compact `CSharpFileIndexed` events.
4. Add `SwarmNeuron` that handles `SwarmTask` and creates a deterministic
   `GroupChatRequested` event for the top relevant files.
5. Add `GroupChatNeuron` with deterministic no-LLM behavior first. It should
   produce `GroupChatDecision` using file summaries and test mappings.
6. Add the first scenario:
   "Swarm plans a testing-framework markdown task without loading every file."
7. Add branch simulation around the plan using existing checkpoint and branch
   primitives.
8. Only after deterministic behavior is solid, add optional LLM reasoning inside
   the group chat.

## First High-Value Scenario

```gherkin
Scenario: Swarm plans a fast testing framework document
  Given a swarm neuron "swarm-main"
  And a codebase snapshot for "framework/NeuroOS.slnx"
  When I assign swarm task "design fast DigitalBrain testing framework"
  Then the swarm creates a focused group chat
  And the group chat decision references tests, simulations, kernel tasks, and branches
  And the selected participant count is less than 12
  And the timeline contains a TestSelection
```

This scenario proves the design goal: a small emergent group can reason over the
codebase without loading every file into one prompt.

## Definition Of Done

The MVP is done when a local test run can prove:

- C# files are indexed into stable file-neuron identities;
- a task selects relevant participants without scanning everything through LLM;
- a group chat decision produces a test plan;
- kernel tasks record the work lifecycle;
- checkpoint/branch simulation is exercised;
- the fast test layer passes without Aspire or Ollama;
- distributed validation remains available for intentional full-system checks.

