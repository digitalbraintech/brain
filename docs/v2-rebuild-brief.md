# DigitalBrain v2 — rebuild brief

**Authority:** this brief is binding for the immediate static-kernel rebuild. Then read
`docs/superpowers/specs/2026-09-02-digitalbrain-v2-durable-runs-design.md`, which is binding for
runtime-created agents, automations, capabilities, activities, and recovery. The older neuron
substrate spec retains the original rationale, and `docs/digitalbrain-v2-anatomy.html` is an
illustrative historical walkthrough; the durable-runs design wins wherever those artifacts differ.
The later self-knowledge, catalog, semantic-search, and intent-resolution slice is governed by
`docs/superpowers/specs/2026-09-02-digitalbrain-v2-self-knowledge-and-ranked-discovery-design.md`.

## Goal

A clean kernel where every type has a caller and every method has one job. Today it does not: there are ~130 lines of provably dead code, a quarter of the contracts package belongs to other modules, and the central class does seven things.

You are rebuilding the substrate, not patching it. Behaviour may change where this brief says it changes.

## Vocabulary — three words, three things

| Term | Is | Is not |
|---|---|---|
| **Neuron** | The actor. A journaled Orleans grain; sole writer of its own state. | A message-handler bag. |
| **Synapse** | A directed, typed, **weighted edge** between two neurons. Potentiates on use, decays at read, is pruned below a floor. | A message. A grain. |
| **Signal** | An immutable record that crosses a synapse. Carries correlation + causation. | A state change. |

The sentence that settles every naming argument: *a neuron fires a signal along a synapse.*

Two more citizens: an **Entity** is a passive durable grain, off the graph, read directly by clients. An **Activity** is the durable trace of one correlation (not built yet).

## Non-negotiable constraints

- **net11.0.** `TreatWarningsAsErrors=true`, `AnalysisLevel=preview-all`, `EnforceCodeStyleInBuild`. An unused `using` fails the build.
- **Central Package Management.** A `PackageReference` carries **no** `Version`; versions live in `Directory.Packages.props`.
- Solution file is **`DigitalBrain.slnx`** (XML). A project not listed there is not built by CI.
- **Every wire type needs `[GenerateSerializer]` + `[Alias("db.…")]`.** Orleans validates the manifest at client construction — a missing attribute throws `CodecNotFoundException` at startup, not at use.
- **Neurons take serialized turns.** `NeuronConcurrency.RequireSerializedTurns` refuses `[Reentrant]`, `[StatelessWorker]`, `[MayInterleave]`, and any `[AlwaysInterleave]`/`[ReadOnly]` outside the kernel's own free reads. Do not weaken it.
- **A grain call to self deadlocks the turn.** There is a self-check in `DeliverToAsync`; anything that delivers must go through one path that has it.
- **Baseline: 141 tests passing, 0 failed.** Run `dotnet build DigitalBrain.slnx -c Release` then `dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1`.

## Delete on sight — proven dead, with evidence

| Item | Lines | Evidence |
|---|---|---|
| `DigitalBrain/SignalTypeIndex.cs` | 69 | **zero references** in `src/` or `tests/` |
| `DigitalBrain/SignalAlias.cs` | 36 | referenced only by `SignalTypeIndex` |
| `DigitalBrain.Contracts/Identity/ModuleId.cs` | 17 | **zero references** |
| `DigitalBrain/Neuron/NeuronTime.cs` | 9 | 2 references, both service-locator sites; **the key it defines is registered by nobody**, so every lookup returns null and falls back to `TimeProvider.System` |
| `SynapseSet.Prune()` | ~15 | no caller, no test — §6's "one reminder per neuron" was never built |

Also: `DigitalBrain.Contracts/Messaging/` still exists holding two files (`DigitalBrainActivated.cs`, `JournalProjectionAttribute.cs`) after everything else moved to `Signals/`. One concept, two folders. Finish the move.

## Move out of the kernel — 11 of 44 contract files belong to modules

`DigitalBrain.Contracts` is meant to hold "signals, synapses, lineage, handle contracts, neuron identity" and depend on `Microsoft.Orleans.Sdk` alone. These are somebody else's domain and the substrate should not know they exist:

| Folder | Files | Used only by | Move to |
|---|---|---|---|
| `Interactions/` (`AgentTurnContext`, `IUserActionSource`, `IUserActionContinuation`, `ITrustedUserCommandHandler`, `IUntrustedContentScreen`, `UserActionRequest`) | 6 | AI, Google, Salesforce, UI, Sdk | a product-contracts package |
| `Execution/` (`ExecutionId`, `ContextPath`, `ContextDigest`) | 3 | `Modules.Execution` only | `Modules.Execution.Contracts` |
| `Security/ProtectedPayloadReference` | 1 | `Modules.Memory` only | `Modules.Memory.Contracts` |
| `Identity/CommandId` | 1 | only `Interactions/` | goes with them |

## The real problem — `Neuron.cs`

322 lines, 26 members, seven responsibilities: activation lifecycle · handler-table reflection and caching · journal read/watch · synapse read · outbound sending · inbound dispatch · telemetry · principal scope.

**Its constructor is four service-locations and a hazard:**

```csharp
protected Neuron()
{
    TimeProvider = ServiceProvider.GetKeyedService<TimeProvider>(NeuronTime.ServiceKey)
                   ?? System.TimeProvider.System;          // key registered by nobody
    _journal  = new NeuronJournal(this, ServiceProvider);  // container passed INTO a collaborator
    _synapses = new SynapseSet(ServiceProvider, Id, TimeProvider);
    _router   = ServiceProvider.GetService<SignalRouter>()
                ?? new SignalRouter(new SignalHandlerIndex());   // silently builds a SECOND router
}                                                                 // with its own private cache
```

That last `??` is a correctness hazard, not a style one: miss the registration and tier-1 routing behaves differently with nothing to tell you.

**Five outbound verbs with no coherent contract:**

| verb | journals | delivers to | records synapse | state write |
|---|---|---|---|---|
| `SendAsync` | yes | one | no | via stage |
| `FireAsync` | yes | one | yes | +1 explicit |
| `BroadcastAsync` | yes ×N | many | yes ×N | +1 after loop |
| `EmitAsync` ×2 | yes | **nobody** (`_ = delivery;`) | no | via stage |
| `ReplyAsync` | yes | fire-and-forget | no | via stage |

`EmitAsync` is a journal write wearing a send verb's name. `BroadcastAsync` bypasses `DeliverToAsync` and calls `GrainFactory` directly, which is why a self-delivery deadlock was possible.

## Target design

### Contracts — split by traffic plane, not by convenience

`Deliver` is plane 1 (signals: journaled, correlated). Reads are plane 4 (direct calls: free, unjournaled). Different planes, different interfaces.

```csharp
/// A graph endpoint. The one thing that makes something a neuron:
/// it accepts a signal and tells you what became of it.
[Alias("db.v2.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<DeliveryOutcome> Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);
}

/// Observation of a neuron. Free: no journal entry, no correlation, safe to
/// interleave because it only ever reads the neuron's own durable state.
[Alias("db.v2.neuron-query")]
public interface INeuronQuery : IGrainWithStringKey
{
    [ReadOnly, AlwaysInterleave, Alias(nameof(ReadJournal))]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);

    [ReadOnly, AlwaysInterleave, Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();

    [Alias(nameof(Watch))]   Task Watch(JournalKind kind, long afterSequence, IJournalObserver observer);
    [Alias(nameof(Unwatch))] Task Unwatch(IJournalObserver observer);
}

/// What became of a delivery. Potentiation happens on Handled and nothing else.
[GenerateSerializer, Alias("db.v2.delivery-outcome")]
public enum DeliveryOutcome
{
    Handled,    // a handler ran to completion — the only outcome that strengthens a synapse
    Unhandled,  // no IHandle<T> for this signal type; must not mint or strengthen an edge
    Refused,    // the membrane refused the turn before any handler ran (blocking receiver)
}
```

**Why the outcome matters.** Today `Deliver` returns `Task`, so a caller cannot tell "a handler ran" from "delivery succeeded and nothing happened". Consequence: firing at a neuron with no `IHandle<T>` mints a `0.65`-weight learned synapse that then feeds tier-2 routing forever. Gating potentiation on `Handled` makes that unreachable.

**Second win:** with reads on their own interface, `NeuronConcurrency` can whitelist by *declaring interface* — a type check — instead of by method-name string (`"ReadJournal"`, `"ReadSynapses"`). Adding a fifth query then never touches the guardrail.

### Three outbound verbs, not five

| verb | meaning |
|---|---|
| `SendAsync(to, signal)` | directed. Journals, delivers, and records the synapse on `Handled`. Recording is **not optional** — that is the graph learning. |
| `BroadcastAsync(signal)` | undirected. The router resolves receivers from the neuron's own synapses (tier 2) plus compile-time `IHandle<T>` handlers (tier 1). Returns how many were reached. |
| `ReplyAsync(response)` | answers the current delivery's caller. Unawaited — the caller's turn is blocked on ours. |

`FireAsync` folds into `SendAsync`. `EmitAsync` is **deleted**; its callers in `SessionNeuron` become an explicit journal write with an honest name, or a `SendAsync`, depending on what each actually meant — read them and decide.

**All delivery goes through one path** that carries the self-check. Two delivery paths is how the deadlock happened.

### Dependencies — constructor injection, no service location

`Neuron` collaborates with **one** injected object rather than resolving four:

```csharp
public sealed class NeuronRuntime(TimeProvider clock, SignalRouter router, SynapseOptions options)
```

registered once in `DigitalBrainRuntime.Add`, with **no `??` fallbacks anywhere**. Subclasses become `protected MyNeuron(NeuronRuntime runtime) : base(runtime) { }` — about 23 of them, mechanical.

Delete `NeuronTime` and its sentinel key. Tests override the clock by registering a fake `TimeProvider`. **This is the change that makes decay and pruning testable at all** — today nothing registers the key, so time cannot be advanced, which is why those behaviours are only ever tested as pure arithmetic on the `Synapse` struct.

`NeuronJournal` and `SynapseSet` must stop taking `IServiceProvider`; give them what they need.

### Is `IDigitalBrain` a neuron? **No — and now for a precise reason.**

`INeuron` means exactly "accepts a `SignalDelivery`, returns a `DeliveryOutcome`". `IDigitalBrain` never accepts a delivery; nobody fires at the facade. It is also `IAsyncDisposable` and owns a cluster client, and a grain can be neither.

What *is* a neuron is **`BrainNeuron`** — the owner's root grain, a genuine graph citizen implementing both `INeuron` and `INeuronQuery`, which mints neurons, holds the owner-level synapse index, and is the journal hub. (Today this is `SessionNeuron`; rename it.)

`IDigitalBrain` is **the client's handle on that neuron**: typed addressing (`Get<T>`, `GetEntity<T>`), lifetime (`IAsyncDisposable`), and a projection of `INeuronQuery`. Every method is a one-liner onto `BrainNeuron` or the grain factory. **It holds zero logic** — if you find yourself writing a branch in the facade, it belongs on the neuron.

Its ownership guard is real but incomplete: `DigitalBrainClient` checks `RequireOwnedSubject`, while `SessionNeuron` and `Neuron.ReadSynapses` check nothing, so a raw grain reference bypasses it. Do **not** add an owner parameter to the interfaces — that lets callers lie. Enforcement belongs in an `IIncomingGrainCallFilter` membrane (design §8.1), which is the next slice's work.

## Order of work

Do these one at a time, full suite green between each.

1. **Contracts.** `INeuron`, `INeuronQuery`, `DeliveryOutcome`, then `Signal`/`SignalDelivery`/`Synapse` reviewed for the same "does every member earn its place" test.
2. **`NeuronRuntime`** + DI registration, no fallbacks. Delete `NeuronTime`.
3. **Decompose `Neuron`** — extract the outbound machinery and the handler-table/dispatch machinery into collaborators. `Neuron` keeps identity, lifecycle, and the `INeuron`/`INeuronQuery` surface.
4. **Collapse the verbs** to three; delete `EmitAsync`; unify delivery through the self-checking path.
5. **Gate potentiation on `Handled`.**
6. **Migrate the 23 subclasses** and all call sites.
7. **Delete the dead list**, finish the `Messaging/` → `Signals/` move, and move the 11 misplaced contract files out.

## Definition of done

- Every type in `DigitalBrain` and `DigitalBrain.Contracts` has at least one caller outside its own file.
- No `GetService`/`GetKeyedService` inside `Neuron`, `Entity`, `NeuronJournal`, or `SynapseSet`.
- No `??` fallback that constructs a dependency the container should have supplied.
- One delivery path; it has the self-check.
- Potentiation happens only on `DeliveryOutcome.Handled`, and a test proves firing at a handler-less neuron mints no edge.
- A test advances a fake clock and asserts decay and pruning through `SynapseSet` — not just on the struct.
- `dotnet build -c Release` clean; full suite green.

## What not to do

- Do not add tier-3 similarity routing, the membrane filter, sensors/effectors, or Roslyn/ALC compilation **to this completed static slice**. Later focused designs own those; side-effect-free ranked catalog discovery is specified separately and still does not auto-route a signal.
- Do not weaken `NeuronConcurrency`.
- Do not put authorization in a contract signature.
- Do not keep a type "because it might be useful" — the whole point of this pass is that everything serves a purpose.
