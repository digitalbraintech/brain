# DigitalBrain

The sentence that settles naming: **a neuron fires a signal along a synapse**.

Two nouns, two verbs, and one query:

- **Neuron** — a durable actor with one receive slot. It owns its outgoing synapses, two
  bounded journals (incoming, outgoing), and the latest signal of each type it received.
- **Signal** — a type name (letters only, vocabulary such as `Note`) plus a JSON body.
  Identity goes in the neuron name, never in the type.
- **Fire** — a neuron emits a type; the signal travels along every synapse of that type.
- **Connect / Disconnect** — create or remove a synapse, the only routing fact in the system.
- **Read** — state, synapses, journals. A query: nothing moves, nothing is journaled.

There is no outside: Claude, a person, and each MCP session are **Session neurons** named
after their principal (default `claude`), and they Fire like anything else.

The MCP server is named `brain`; its four tools — `fire`, `connect`, `disconnect`, `read` — are served at `/mcp` by the kernel
silo, on port 5080 under `aspire start`. `DigitalBrain.Mcp` is the client; the tests call the
same four operations as plain C#.

Read next: [01 communication model](docs/architecture/01-communication-model.md),
[02 working-style memory](docs/architecture/02-working-style-memory.md),
[03 MCP surface](docs/architecture/03-mcp-surface.md),
[04 testing and project shape](docs/architecture/04-testing-and-projects.md), and the
usability bar in [the Grok benchmark](docs/architecture/grok-benchmark.md).
