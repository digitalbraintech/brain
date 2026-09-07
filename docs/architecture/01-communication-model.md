# 1. Communication model

Status: approved 2026-09-08. Supersedes the vocabulary in `CONTEXT.md` where they differ.

## Two nouns, two verbs

**Neuron.** A durable actor with one receive slot: `Task Receive(Signal)`. It has no
return value. If it has something to say, it Fires. A neuron owns its outgoing
synapses, two bounded journals (incoming, outgoing), and the latest signal of each type
it has received, kept durably outside the journal window. That last item is its state in
the most literal sense: what was last said to it.

**Read.** Not a verb of communication. State, synapses, and journals are queries: nothing
moves, nothing is journaled, no synapse is involved.

**Membrane.** A signal payload is capped at 64 KB. Larger signals are rejected before
delivery with no journal entry on either end. Latest-per-type is unbounded in count, so the
cap is what keeps any neuron's durable footprint in the low megabytes.

**Synapse.** A directed edge `A -> B` for one signal type `T`, stored on `A`. It is the
only routing fact in the system. Whether `B` receives `T` from `A` is decided entirely by
whether that synapse exists. There is one kind of synapse.

**Fire.** A neuron emits `T`. The signal travels along every synapse of type `T` on that
neuron. Point-to-point is a synapse with one target. Pub/sub is a synapse with many.
Send, Publish and Broadcast are the same verb. The emitter never receives its own fire.

**Connect / Disconnect.** Create or remove a synapse. Replaces Subscribe/Unsubscribe.

## No outside

There is no injection verb. The caller is on the graph. Claude, a person, and each MCP
session are Sessions, which are neurons. A Session Fires like anything else, and what
comes back lands in its own incoming journal. Sensory input (webhooks, timers) will be
neurons that Fire too.

## Request/reply is a pattern, not a primitive

`A` fires `Question` along `A -> B`. `B` fires `Answer` along `B -> A`. The envelope
carries correlation. "Ask and wait" is a client convenience (`read` with a timeout): poll the caller's incoming
journal for the next signal with that correlation, with a timeout. A conversation
therefore needs synapses in both directions, created explicitly.

## What was deleted, and why

| Removed | Reason |
|---|---|
| `IHandle<T>` | Put "can B receive T" on the type as a compile-time gate. In this model that fact lives on the synapse. The type may *advertise* what it reacts to for discovery; an unknown signal is journaled and ignored, nobody throws. |
| `IContext`, `ExecutionId`, `ExecutionIdentity`, `ExecutionOutcome`, `ExecutionScope`, Sdk `Context`, `IDeferredReply`, `SignalRequestPolicy`, step checkpoints, `CompleteAsync` | Existed only because compiled handlers ran outside the silo in a scripting worker. In core every handler runs inside its own Orleans turn. When compiled behaviors return, Roslyn compiles into the silo and the delegate runs in the turn. |
| `DeliveryOutcome`, `SignalDeliveryResult`, the unhandled path | A neuron that does not react to a signal is not an error condition; the signal is journaled and ignored. |
| Learned synapses, `SynapseKind`, fire counts, "handled send learns" | Nothing read them. Routing follows explicit synapses only. "Who talked to whom" is the outgoing journal. |
| `Broadcast` grain method, `Subscribe`/`Unsubscribe` signals | Folded into Fire and Connect/Disconnect. |
| `OwnerId`, `PrincipalId`, `PrincipalPartition`, `ActorContext`, `NeuronAuthorizationException`, owner-root `BrainNeuron`, Silo auth | One brain per silo. `NeuronId` is type + name. Who may Connect to what is MCP policy, not identity. |
| Entity, `IEntity`, `EntityId` | A profile or chart is a neuron that holds state and has few synapses. See storage below. |
| `Watch`, `Unwatch`, `IJournalObserver` | Semantic observation is a synapse: connect and read your own inbox. Infrastructure observation is Orleans telemetry. |

## Storage alignment

`Neuron` is an Orleans `DurableGrain` (Orleans.Journaling, preview). Anatomy, meaning
synapses and both journal windows, always lives in the per-grain operation log: append-heavy,
bounded (512 entries / 512 KB per window), replayed on activation, compacted periodically.

`Neuron<TState>` adds an `[PersistentState]` snapshot facet with `State` and `SaveAsync`.
The subclass chooses:

| State shape | Mechanism | Example |
|---|---|---|
| Small, incremental | durable value / collection in the op log | a rule's confirmation count |
| Kilobytes, replaced wholesale | snapshot | profile, memory document |
| Unbounded growth | neither: many neurons (per bucket) or an external store the neuron points at | time series |
| None | base `Neuron` | a Session |

Journal append (op log) and snapshot save in one `Receive` are two providers and not one
transaction. This was already true between an Entity save and the caller's journal. If
Orleans.Journaling proves unreliable, anatomy moves into a snapshot; the bounded journal
keeps that blob at about 1 MB per neuron.

## Observation

Journals are for neurons. Telemetry is for eyes. Core adds no observation hook of its own:

- `AddActivityPropagation()` plus the `Microsoft.Orleans.Application` `ActivitySource` in
  OpenTelemetry gives one span per `Receive`, with parent linkage across a chain of fires.
  Visible in the Aspire dashboard with no code of ours.
- Later, one `IIncomingGrainCallFilter` can tag spans with signal type and neuron names.
- Later, an Orleans broadcast channel plus an SSE endpoint on the Silo host feeds a live
  graph view. Nothing in core changes for it.

## Invariants for tests

1. A fire reaches exactly the targets of that neuron's synapses for that signal type, never the emitter.
2. With no synapse there is no delivery, regardless of what any type advertises.
3. Every delivery is journaled on both ends with signal id, correlation, sequence, timestamp, and source.
4. A neuron receiving a signal it does not react to journals it and does nothing else.
5. Connect is idempotent; Disconnect of a missing synapse is a no-op.
6. Journals stay within their retention bounds; sequences and tallies survive compaction and restart.
7. A `Neuron<TState>` snapshot survives restart and is not present in any journal.
