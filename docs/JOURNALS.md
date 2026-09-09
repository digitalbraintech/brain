# Journals, Durable State, and History — who owns what

Three concepts share the word "journal" in this codebase's ancestry. This is the contract:

| Concept | Owner | What it is | Retention |
|---|---|---|---|
| Traffic journal | Every **neuron** (only neurons) | Incoming/Outgoing `SignalDelivery` feeds: sequence-numbered observation windows with per-signal-type tallies | Bounded: 512 entries / 512 KB per feed; reads past retention return a `ResetSnapshot` |
| Synapse | The **source neuron** (durable state, not a grain of its own) | A directed, typed, weighted edge to another neuron — durable anatomy, not traffic. Strengthens (potentiates) on a successful fire and decays by read-time half-life; not a journal entry and not sequence-numbered | Not bounded by the 512-entry/512 KB traffic cap; read-time decay and pruning (below a weight floor) manage its size instead |
| Durable state | Every durable grain (neurons AND entities) | Orleans.Journaling persistence (`IDurableValue`/`IDurableList` over the `journal` blob connection) — infrastructure, not a domain concept; hosted via `DurableStateHosting.AddDigitalBrainDurableState` | Managed by Orleans.Journaling (append + compaction) |
| Memory facts | The **owner** (or principal) | Watermarked, resumable story facts (`IFactMemory`) — long-term history, part of the Memory module alongside vector memory | Bounded: 4096 facts, oldest dropped first |

## The rules

1. **Neurons own traffic journals. Entities own snapshots. Memory owns history.**
   An `Entity<TState>` (`DigitalBrain.Core`) is a plain stateful grain: `Read()`/`SaveAsync()`
   over durable state. It has no journals and no synapse membrane, and it is never a
   synapse-graph endpoint.
2. **The brain neuron is the owner's journal hub.** `IBrainNeuron.ReadNeuronJournal` and
   `WatchNeuron` proxy access to subject neurons. Its persisted address remains
   `sessionneuron` / `session`. The kernel SSE helper `OwnerSessionJournal` watches the
   named chat or UI renderer neuron's Outgoing journal through `IDigitalBrain`.
3. **Writes journal, reads don't.** Entity mutations are driven by neurons: a neuron fires a
   signal along a synapse, the handling neuron mutates the entity, and that neuron's Outgoing
   journal records the effect. Clients and UI read entities directly
   (`IDigitalBrain.GetEntity<TEntity>()`) — free and unjournaled.
4. **The word "journal" in domain code always means the traffic journal.** The persistence
   infrastructure uses durable-state language (`DurableStateHosting`, `DurableStateJson`,
   `DigitalBrainBuilder.DurableStateStore`). The blob resource/connection is still literally
   named `journal` (`DigitalBrainNames.Journal` / `JournalConnection`) — a deployed-name
   compatibility constraint, not vocabulary.
5. **Orleans grain call filters are not a journal and not a synapse writer.** Incoming/outgoing
   filters may wrap the membrane (auth, principal, trace). They must not append feeds or
   `Reinforce`/`Bind` synapses: self-send never hits a grain proxy, Subscribe is explicit
   `BindOutgoing`, and query methods interleave. `SignalSender` appends outgoing deliveries
   and reinforces synapses after a handled send. `Neuron` appends incoming deliveries and
   binds or unbinds outgoing synapses.

## Semantics pinned by tests (phase 2)

Resume sequences, the reset-snapshot path at the retention boundary, tallies,
checkpoint/restore, and watcher-drop behavior are pinned by the Tier 2 simulation suite
(`DigitalBrain.Testing`) — see the design spec §6 and §9.
