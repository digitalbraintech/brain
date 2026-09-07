# 3. MCP surface

Status: approved 2026-09-08. Builds on [01-communication-model.md](01-communication-model.md)
and [02-working-style-memory.md](02-working-style-memory.md).

This is what Claude Code, Grok CLI, or any other MCP client sees. Four tools that map
one-to-one onto the model. Signals are a type name plus a JSON body; no C# is needed to
use any of it. Every tool acts as, or reads through, the caller's Session neuron.

## Tools

| Tool | Arguments | Does |
|---|---|---|
| `fire` | `type`, `body`, optional `to`, optional `correlation` | Emits the signal from the Session. Without `to`: along every Session synapse of that type. With `to`: along exactly that one synapse, creating it first if missing. Returns signal id and correlation. |
| `connect` | `from`, `to`, `type` | Creates the synapse. Idempotent. Neurons that do not exist yet come into being by being named. |
| `disconnect` | `from`, `to`, `type` | Removes the synapse. No-op if absent. |
| `read` | `neuron`, optional `what`, optional `after`, optional `timeoutSeconds` | Returns a view of the neuron. `what` is one of `state`, `synapses`, `incoming`, `outgoing`; omitted means all four. `after` is a journal sequence. `timeoutSeconds` makes an `incoming`/`outgoing` read block until a new entry appears or the timeout elapses; it is clamped to 60 seconds. A default read (no `what`) spends one deadline across both journals, not one each. |

## Decisions

**Directed fire is fire along a subset.** The model verb is "emit T along every synapse of
type T". `to` narrows that to one edge. It is not injection: the edge exists afterwards and
both journals show the delivery. The doc says this rather than pretending the directed case
does not exist.

**There is no `create`.** A neuron exists when it is named. Orleans activates on first touch,
and a neuron with no traffic and no synapses costs nothing. The graph is whatever has been
named.

**There is no `wait`.** Waiting is `read` with `timeoutSeconds`: poll the journal after a sequence,
return the next entry or time out. The ceiling is 60 seconds, because a read is a query and not a
subscription; a client that wants to wait longer reads again. In the memory flow it is never needed. It exists for
future reactive neurons and for two Sessions talking.

**`read` defaults to everything.** The recall pattern is: read a topic, follow its synapses,
read each target. One round trip per step matters more than a minimal payload.

**Errors say what to do next.** Every rejection is one short sentence with the corrective
action. This is the part an unfamiliar model will test hardest.

| Condition | Message shape |
|---|---|
| payload over 64 KB | "Signal body is N KB; the limit is 64 KB. Split the content across neurons or store it externally and fire a reference." |
| type name is not vocabulary (contains dates, ids, spaces) | "Type names are vocabulary such as `Note`; put identity in the neuron name." |
| `read` on a neuron never touched | returns an empty view, not an error; the neuron now exists |
| `disconnect` of a missing synapse | success, no-op |

## Session identity

One Session neuron per principal. The client passes the principal at connect time; the
default is `claude`. Two Claude Code windows share one Session unless they say otherwise.
Your working style is one graph, not one per window.

## Not here

- No `list neurons`. The graph is walked from Sessions and topics. A global list arrives
  when there is a registry of reactive types to browse.
- No auth. One brain per silo.
- No separate C# SDK. The four operations in `DigitalBrain.Mcp` are the client; tests call
  them directly as plain C# (see section 4).

## Usability benchmark

Grok CLI is the external client that exercises this surface end to end. The test is
whether a model that has never seen this repository can, from the tool descriptions alone,
store a working-style note, group it under a topic, recall it after a restart, and correct
it. Tool descriptions and error messages are tuned until it can.

## Invariants for tests

1. `fire` with `to` leaves exactly one new synapse Session→`to` for that type, and one delivery.
2. `fire` without `to` delivers to every Session synapse of that type and nothing else.
3. `connect` twice is one synapse; `disconnect` of nothing succeeds.
4. `read` never changes any journal, tally, or synapse on any neuron.
5. `read` with `timeoutSeconds` returns as soon as a matching entry arrives, and returns empty at the deadline.
6. A rejected fire (payload cap, bad type name) produces no delivery and no journal entry on either end.
7. Two connections with the same principal fire from the same Session neuron.
