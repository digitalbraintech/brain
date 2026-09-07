# 4. Testing and project shape

Status: approved 2026-09-08. Closes the core design started in
[01-communication-model.md](01-communication-model.md).

## Two test layers

| Layer | Where | Drives the brain through | Covers |
|---|---|---|---|
| Behaviour | `tests/DigitalBrain.Tests`, Reqnroll, in-memory cluster | the same four operations Claude uses, called as plain C# on `DigitalBrain.Mcp` | every invariant in sections 1 to 3, every error message |
| Usability | a script, not a test project | the running Silo, via Grok CLI over MCP | the benchmark in section 3, scored by hand |

There is no separate "model" layer. The four operations are the model's surface, so testing
through them tests both at once. One feature file per behaviour, one grain type per fixture,
the style the current suite already uses. The existing four features are rewritten, not
patched: their vocabulary (IHandle, Learned, Broadcast, IContext) no longer exists.

## Features

| Feature | Scenarios |
|---|---|
| `fire` | along all synapses of a type; along one with `to`; never the emitter; a signal the neuron does not react to is journaled and ignored |
| `connect` | connect is idempotent; disconnect of nothing is a no-op; a neuron exists when named |
| `journal` | envelopes on both ends; window bounds; tallies and sequences across compaction and restart |
| `state` | latest-per-type; replacement; survives restart; `Neuron<TState>` snapshot never appears in a journal |
| `membrane` | payload cap; type-name rules; a rejected fire leaves no trace on either end |
| `read` | read is a query and changes nothing; default view; walk from a topic; `timeout` semantics; one Session per principal shared across connections |

## Projects

| Project | Role |
|---|---|
| `DigitalBrain.Contracts` | `INeuron` with `Receive`, `Signal`, `NeuronId`, `Synapse`, journal records. Separate only because Orleans wants grain interfaces in their own assembly. |
| `DigitalBrain` | the runtime: `Neuron`, `Neuron<TState>`, router, journals, synapses, latest-per-type, membrane |
| `DigitalBrain.Mcp` | the client. The four operations as plain C# methods over records, with MCP tool wrappers, the JSON signal codec, and Session mapping. Tests and tools call the same methods. |
| `DigitalBrain.Silo` | thin host: Orleans plus the MCP endpoint |
| `DigitalBrain.Testing` | the testing framework: in-memory cluster fixture, durable-storage restart helpers, fixture neuron types, step helpers. Kept as its own project so future neuron packages test against the same harness. |
| Aspire folder (4 projects) | untouched |
| `tests/DigitalBrain.Tests` | the feature files and steps, renamed from `DigitalBrain.Substrate.Tests` |

Deleted: `DigitalBrain.Sdk`. Its reason to exist, a typed client, disappeared with `IHandle`;
the four operations in `DigitalBrain.Mcp` are the client now.

Not brought back in this phase: Scripting, AI, Memory, Time, SmartPrompt, Integrations,
UI/Flutter. Each returns later as a package of reactive neuron types that plug into the
same four operations. Scripting first.

## Order of work

1. Cut Contracts and the runtime down to the model. `fire`, `connect`, `journal` green.
2. Add latest-per-type and the membrane cap. `state`, `membrane` green.
3. Build `DigitalBrain.Mcp` with the tests driving its operations directly. `read` green.
4. Host the MCP endpoint in the Silo, start under Aspire, run the Grok CLI benchmark, tune
   tool descriptions and error messages until an unfamiliar model completes it.

Each step ends with the whole suite green and a commit. No step depends on the Aspire
projects changing.
