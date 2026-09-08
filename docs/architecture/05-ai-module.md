# 5. Reactive neurons and the AI module

Status: approved 2026-09-08. Builds on [01-communication-model.md](01-communication-model.md)
and [03-mcp-surface.md](03-mcp-surface.md). Supersedes the `ScheduleTurn` paragraph in doc 01
and the AI rows in doc 04 that commit `7e55ef26` introduced.

Standing rule (also stored in the brain under `way-of-working -> ai-uses-maf`): AI is
implemented with Microsoft Agent Framework (MAF) agents on top of `IChatClient`. Agents
understand tools. The brain stays the source of truth for participants, transcript and
visibility; MAF is the execution engine.

## The inbox drain (core)

`Deliver` only **accepts**: journal incoming, set latest-per-type, persist, return. The base
`Neuron` then drains its inbox in its own turns: a durable cursor "reacted up to sequence N",
one `ReceiveAsync` per entry in journal order, cursor advanced after each. A one-way self-call
wakes the drain; activation resumes it.

Consequences:

- A neuron may `FireAsync` anywhere from inside `ReceiveAsync`, including back at its source.
  The source's `Fire` completed at accept time, so there is no cycle. `NeuronRequestPath` and
  `ScheduleTurn` are deleted.
- `FireOutcome.Delivered` means "accepted by N neurons".
- A crash between accept and reaction replays the unreacted entries. A throwing `ReceiveAsync`
  leaves the cursor in place; the next drain retries and telemetry records the failure.
- Rule for reactions: inside a turn a neuron may fire and read, never wait. Whatever it is
  waiting for arrives as its next incoming signal with the same correlation.

Invariants: every accepted signal is reacted to exactly once across restarts; reactions run in
journal order per neuron; a neuron can fire at its source from inside `ReceiveAsync`; `Fire`
returns once every target has accepted; a throwing reaction does not lose the entry.

## What an agent is

An `agent` neuron is a Session neuron with a model attached.

- `Neuron<AgentState>`. `Instruct {provider, model, system, tools}` is its configuration,
  held as latest-per-type. The snapshot holds serialized MAF sessions keyed by correlation,
  bounded to the most recent N.
- Reacts to `Ask` and `Turn` by running a MAF `ChatClientAgent` over the provider's
  `IChatClient`, then fires `Reply` (for `Ask`) or `Said` (for `Turn`) back at the source with
  the same correlation. Everything else is journaled and ignored.
- **Tools are the four brain operations.** Every agent gets `fire`, `connect`, `disconnect`,
  `read` as `AIFunction`s bound to its own neuron identity, so it can recall memory, read a
  transcript, or talk to any neuron exactly as Claude does. Native tools from other modules are
  `AIFunction`s registered by name and enabled through `Instruct.tools`. There is no `ToolCall`
  signal and no tool registry in core.
- The journal records what crossed the graph (its tool `fire`s show in its outgoing journal
  like any Session's). The snapshot is its private working memory, including the inner
  function-call loop.

## What a group chat is

Participants, a transcript, and a turn policy.

- **Participants are `agent` neurons.** Different LLMs in one chat is different `Instruct`
  bodies, nothing else.
- **The transcript is the `chat` neuron's incoming journal**, filtered by correlation.
- **The turn policy is MAF's `GroupChatManager`** (round-robin or LLM-selected), run by the
  `chat` neuron as a MAF group-chat workflow whose participants are **`RequestPort` proxies**.
  When the manager picks a speaker the workflow halts with an external request; the `chat`
  neuron checkpoints into its `Neuron<ChatState>` snapshot and fires `Turn` at that participant.
  When `Said` arrives it resumes the workflow from the checkpoint with that text as the
  response. When the workflow completes it fires `Reply` at whoever sent the `Ask`.
- **Who hears is anatomy.** On `Instruct {participants, manager, rounds}` the `chat` neuron
  wires `chat --Turn--> p` and `p --Said--> chat` for each participant. A session that wants to
  watch live connects `p --Said--> session` itself, or reads the chat journal afterwards.
- A participant's turn is a read-walk plus one fire: it reads the source's incoming journal for
  that correlation to build context, then fires `Said`. `Turn` carries no transcript.

```
session --Ask--> chat                             (corr C)
chat: workflow halts on RequestPort(writer); checkpoint; --Turn--> writer (C)
writer: reads chat.incoming[C]; --Said--> chat (C)
chat: resume; halts on RequestPort(reviewer); checkpoint; --Turn--> reviewer (C)
...
chat: workflow complete; --Reply--> session (C)
```

## Vocabulary

`Instruct`, `Ask`, `Reply`, `Turn`, `Said`. The Contracts project documents their body shapes;
it ships no C# signal types.

## Module shape

| Project | Role |
|---|---|
| `DigitalBrain.Modules.AI.Contracts` | grain type names (`agent`, `chat`); documented body shapes for the five words; nothing executable |
| `DigitalBrain.Modules.AI` | `AgentNeuron`, `ChatNeuron`, `AIModule` (one `IChatClient` per configured provider, native tool registration) |
| `DigitalBrain.Modules.AI.Aspire.Hosting` | `AddModule<AIModule>()` for the AppHost, provider keys and endpoints as parameters |

The AppHost calls the module's own hosting extension; nothing in `src/Aspire` is edited by hand.

## Deleted from commit `7e55ef26`, and why

| Removed | Reason | Cost if wrong |
|---|---|---|
| `ScheduleTurn`, `NeuronRequestPath` | the inbox drain makes fire-back the normal case; timers are non-durable and invisible | none |
| in-grain agents in `groupchat` | a hidden graph inside a node; replaced by `RequestPort` proxies over real neurons | one resume round-trip per turn |
| `SignalText.TryShow`, `Choice`, `Show` | UI vocabulary inside an LLM neuron | callers define their own |
| removal of the 64 KB body cap | latest-per-type does not compact; the window bounds retained entries, not `latest` | none; the cap is restored |
| the hand edit of `AppHost.cs` | module registration belongs to the module's Aspire.Hosting project | none |

## Invariants for tests

1. One `Reply` per `Ask`, carrying the `Ask`'s correlation.
2. `Said` count equals the manager's turn count for that correlation.
3. Two concurrent `Ask`s never mix sessions or transcripts.
4. The transcript is readable from the `chat` journal with no participant alive.
5. A chat survives a silo restart mid-conversation and continues from its checkpoint.
6. An agent's tool `fire` appears in its outgoing journal like any Session's.
7. A participant whose client throws does not lose the turn: the drain retries.
