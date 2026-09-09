# DigitalBrain

A personal assistant whose durable graph a user (or the assistant) programs with typed C#.

The sentence that settles naming: **a neuron fires a signal along a synapse**.

## Graph

**Neuron**
A durable actor. It receives and emits typed Signals, owns its Synapses and journals, and keeps its own state.
_Avoid_: agent, service, grain (product language)

**Signal**
A typed, immutable message. Identity, causation, correlation, and ownership ride the delivery envelope, not the payload.
_Avoid_: event, bus message, “synapse” as a message

**Synapse**
A directed, typed, weighted edge between two Neurons. Lives on the source. `SubscribeTo` writes a Bound edge (does not decay). A handled Send writes a Learned edge (decays). Anatomy, not traffic.
_Avoid_: message, subscription grain, journal entry

**Journal**
A bounded window over a Neuron’s incoming or outgoing Signals. How scripts notice that something happened.
_Avoid_: event store, execution history, a record of Synapses

**Entity**
A live snapshot (Chart, Surface, Memory). Direct typed reads/writes. Not on the graph: no journal, no synapses, not a signal target.
_Avoid_: neuron, run history

**IDigitalBrain**
The owner’s typed handle: `Get<TNeuron>`, `GetEntity<TEntity>`, journals. The assistant and scripts use this, not Orleans.

In code, a `Neuron` owns its outgoing relationships through `NeuronSynapses` and its incoming/outgoing journal windows (`JournalWindow`) through `NeuronJournals`. Synapses `Bind`, `Unbind`, and `Reinforce`; journals record signal deliveries.

## Programming

**Script**
User- or assistant-authored C#, compiled against module contracts, executed outside the silo.

**Behavior**
A named `IBehavior` neuron that owns a saved C# handler, its validated draft and active revision,
subscriptions, durable accepted inputs, execution claims, request checkpoints and output delivery.
The scripting host runs handlers outside serialized neuron turns. Each accepted input pins its
revision. Saving changes creates a draft; activation affects future inputs. Disable removes its
incoming subscriptions and fences outstanding execution and output.

Trigger is type-safe: you may `Send`/`Publish` `TSignal` only to a neuron that `IHandle<TSignal>`s it.
`IHandle<T>` is the capability to receive T. A **synapse** is who actually receives T from **this** source.
`SubscribeTo<TSource, TSignal>(sourceId)` writes that synapse (durable, does not decay). Broadcast fires only along those synapses — not to every neuron type that `IHandle`s T.

```csharp
await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
var inbox = digitalBrain.Get<IUserMessages>("default");
var memoryAgent = digitalBrain.Get<IBehavior>("memory-agent");
await memoryAgent.SaveScriptAsync<UserMessaged>(handlerSource);
await memoryAgent.SubscribeToAsync<IUserMessages, UserMessaged>(inbox.Id);
await memoryAgent.ActivateAsync();
```

English is how the owner asks. A compiled script is what they get. There is no second runtime, grant catalog, or JSON capability bus for this path.

Start with [Flutter chat and personal C# review routines](docs/GETTING_STARTED.md).
The assistant can save, inspect, validate, connect, activate, invoke and disable behaviors.
Each definition lives on its own `BehaviorNeuron`; `BehaviorsNeuron` is the discovery index.
Its notifications wake the separate scripting worker;
durable recovery handles missed notifications. Composition commands are not replayed on restart.
The former admitted-script runtime and fixed GitHub review pipeline have been removed.
Development chat can read the configured local repository diff for a one-off review.

The SDK owns the concrete client and reusable `WebhookNeuron`. A thin authenticated HTTP
adapter durably accepts receipts before acknowledging them. Provider modules translate those
receipts into minimal typed domain facts. `IRepository : IWebhook` is the GitHub source;
there is no mandatory second webhook neuron or GitHub-specific review orchestrator.
See [the implementation contract](docs/programmable-behaviors-implementation.md).

## Specialist modules

Ino delegates to `IAspire`, `IGmail`, and `ISalesforce`. Each inherits `IAgent`
(`IHandle<AgentRequest>` with `AgentReply`) and owns its native discovered MCP tools.
An ordinary request uses the initiating neuron's source-owned send path and can
create a Learned synapse; it does not create a Bound subscription.

Google, Salesforce, and Microsoft own connection policy and static presentation
metadata. The SDK owns MCP sessions/discovery; the shared AI tool boundary owns
screened evidence. Provider operation schemas remain MCP-owned.

`AgentActivity` records diagnostic journal evidence, not subscriber delivery.
Unsubscribe removes the current edge; a later explicit handled send can establish
a Learned edge that is again eligible for broadcast. Journals remain bounded.
