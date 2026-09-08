# 2. Working-style memory

Status: approved 2026-09-08. Builds on [01-communication-model.md](01-communication-model.md).

The first flow the brain must express: the user describes how they want Claude to work,
Claude stores it, finds it again at the right moments, and revises it when corrected.

It needs **no new neuron types and no fixed signals**. Memory is the communication model
used well.

## What a neuron is, completely

- a **name**
- its **synapses** (outgoing, typed)
- its two bounded **journals** (incoming, outgoing)
- the **latest signal of each type** it has received, kept durably and outside the journal
  window. This is the neuron's state in the most literal sense: what was last said to it.

The last item is the one addition core makes for memory.

## Three operations, and which side of the line they sit on

| Operation | Kind | Touches the graph? |
|---|---|---|
| Fire | communication | yes: delivery, journals on both ends |
| Connect / Disconnect | communication | yes: anatomy changes |
| Read (state, synapses, journals) | query | no: nothing moves, nothing is journaled |

Recall is a read-walk. Nothing has to react, reply, or forward.

## Flows

**The user describes a way of working.** "Always run tests before committing."
Claude fires `Note { text }` at a neuron it names `run-tests-before-commit` and connects
the topic neuron `git` to it. The topic is a plain neuron whose only job is to have synapses.

**Session start or a new task.** Claude reads `git`'s synapses, follows them, and reads the
latest `Note` on each neuron it reaches. It applies what it finds using its own judgment.

**The user confirms.** Claude fires `Confirmed {}` at the note. The neuron's tally for
`Confirmed` increments. Nothing else changes.

**The user corrects.** Claude fires a newer `Note` at the same neuron. Latest-per-type now
returns the new text. The incoming journal still holds the old one for as long as the
window keeps it.

**Inspection.** Claude walks the graph from its own Session neuron and explains the user's
working style from names, synapses, and latest notes alone.

## Vocabulary belongs to Claude, structure belongs to the graph

Core ships no signal types. Claude invents them: `Note`, `Confirmed`, `Decision`,
`Preference`. The graph records whatever it chooses. Two conventions keep this healthy:

- **Type names are vocabulary, not identity.** `Note`, never `note-2026-09-08`. Identity goes in
  the neuron name. Latest-per-type is keyed by type name and would grow without bound otherwise.
- **History beyond the window is anatomy.** A version per neuron, a neuron per day, connected
  together. Graph structure is unbounded and inspectable; a single neuron's log is not meant to be.

## Journal bounds, and why they are safe here

Each window keeps at most 512 entries or 512 KB. Overflow drops the oldest. Sequences and
per-type tallies survive. Recall never depends on the window, because it reads latest-per-type.

| Neuron | Traffic | Outcome |
|---|---|---|
| a note | a handful of signals ever | never near the cap |
| a topic | receives nothing, only has synapses | empty journals |
| Claude's Session | every tool call fires from it | outgoing window turns over; old traffic drops, nothing recall needs is lost |
| a future reactive neuron | high fan-in | window turns over; tallies and latest-per-type intact |

**Large content.** Latest-per-type is durable and outside the window. Bodies that do not
fit the journal window are split across neurons or live in an external store the signal
points at. There is no separate payload cap in core.

## Invariants for tests

1. Firing `T` at a neuron makes that signal readable as its latest `T`, after restart too.
2. Firing a second `T` replaces the latest `T`; the earlier one remains in the incoming journal while the window retains it.
3. Tallies count every received signal per type and survive window overflow and restart.
4. Reading state, synapses, or journals produces no delivery and no journal entry anywhere.
5. A walk from a topic reaches exactly the neurons its synapses point at.
6. A signal whose type is not vocabulary, or whose body is not JSON, is rejected before delivery, with no journal entry on either end.
7. A neuron with no synapses and no traffic reads as empty and costs nothing beyond its name.
