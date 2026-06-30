# Design: Telegram LLM Bot as an Installable, Self-Configuring Experience

**Date:** 2026-06-30
**Repo:** `digitalbraintech/framework` (`brain/`), with a thin slice in `digitalbraintech/app` (`app/`)
**Status:** Design — pending user approval before planning

---

## 1. Goal

Ship a Telegram bot as a **marketplace experience**: you install it, it asks you for a bot
token and (optionally) an LLM key via an in-app form, and from that moment it answers your
Telegram messages using an LLM — **local Ollama by default**, no AppHost restart.

The experience is the forcing function for three capabilities the runtime is missing or has
**regressed**:

1. **Reactive broadcast** — a fired synapse reaching every handler that declares it, so a
   freshly-installed neuron reacts without restart. This is currently an **empty stub** in
   `brain/` and must be restored from the archive.
2. **Install-time config/secrets prompting** for packs.
3. A **capability-transport** pattern letting a sandboxed pack participate in real-world I/O
   without breaking the sandbox.

The architecture/distribution assessment and the reactive-neuron use cases are folded in
where they bear on the build.

---

## 2. Constraints that shape the design (verified against the code)

- **The sandbox.** A `NeuroPack` is compiled C# scanned by `CapabilityGate`
  (`DigitalBrain.Kernel/Foundry/CapabilityGate.cs`), which bans `System.Diagnostics.Process`,
  `System.Reflection.Emit`, `System.Runtime.InteropServices`, `System.Runtime.Loader`,
  `Microsoft.Win32.Registry`. A pack also implements `IPackBehavior`
  (`DigitalBrain.Core/Distribution/IPackBehavior.cs`), which is **pure and synchronous with
  no injected services** (`string Respond(string)`, `IReadOnlyList<Synapse> Handle(Synapse)`,
  `PackManifest GetManifest()`), instantiated via a parameterless ctor in a collectible ALC.
  **A pack cannot do network I/O and cannot call an async `IChatClient`.**
- **Aspire resources are static.** Adding an executable/container to the AppHost graph needs a
  rebuild + restart. "Install and it's instantly live" exists only at the neuron/pack layer.
- **Broadcast is an empty stub — REGRESSED.** `DigitalBrain.Kernel/Neuron.cs` `FireAsync` has
  `if (stamped.IsBroadcast) { }` — *empty*. Delivery today is point-to-point only (by
  `Receiver` grain key, or to self). There is no fan-out, no handler registry, no manifest
  routing. `HomeFeedBus` is RFW-card fan-out to UI gRPC clients, **not** a neuron synapse bus.
  The proven mechanism lives in `Projects/final` ≈ `Projects/self-improving`: a single Orleans
  **timeline stream** that grains subscribe to on activation **iff** they declare a handler.
  `brain/` kept the types but dropped the subscription. **We restore it.**
- **The transport↔kernel boundary is already string-typed.** `Send` crosses a `type_name` +
  JSON `payload` (`DigitalBrain.Kernel/Gateway/GatewayService.cs`), not a CLR type. Domain
  synapse types never need to exist in `Core` or `Kernel`.
- **`DigitalBrain.Core` is pure protocol.** No domain types. Telegram contracts do not go here.

---

## 3. Architecture

**Governing rule: dependency inversion + a domain-free kernel.** The kernel is a generic
synapse router/host. Every Telegram-specific atom lives in `DigitalBrain.Telegram` and depends
only on the kernel's type-agnostic abstractions. Arrow points one way: `Telegram → kernel`.
Swap Telegram for Discord and **no kernel line changes**.

```
        DigitalBrain.Telegram ──▶ generic kernel/core abstractions  (one-way)

┌─ kernel — generic synapse router/host, zero domain knowledge ─────────────────────┐
│  RESTORED (harvest Projects/final, reflection-only — NO source-gen manifest):       │
│    • Neuron.OnActivateAsync: subscribe to the timeline stream IFF IHandle<T>         │
│      declared (ResumeAsync on reactivation — avoids dup subscriptions)               │
│    • Neuron broadcast: fill if(IsBroadcast){} → Timeline().OnNextAsync(stamped)      │
│    • Neuron.OnNextAsync → reflection dispatch to IHandle<T>.HandleAsync              │
│  GENERIC PRIMITIVES (domain-free):                                                  │
│    • Send(envelope{type,props}) → fire a generic Signal onto the timeline            │
│    • WatchSynapses(typeFilter[]) → stream<envelope>   (egress bus; ⟂ HomeFeedBus)    │
│    • Signal(Name, Props) carrier — pack-defined "types" ride as name+props           │
│    • AskLlm(Prompt, ReplyType, ReplyProps) — generic LLM-intent synapse              │
│    • LlmResponderNeuron : IHandle<AskLlm> → IChatClient → Signal(ReplyType, …)       │
│  ADAPTED:                                                                            │
│    • GeneratedNeuron subscribes to timeline ALWAYS (dynamic handler) and filters     │
│      each broadcast through its embodied pack's manifest (_embodied.CanHandle)        │
└─────────────────────────────────────────────────────────────────────────────────────┘
                      ▲                                   ▲
          Send / WatchSynapses (gRPC)        embodied pack rides the timeline broadcast
                      │                                   │
┌─ DigitalBrain.Telegram — owns 100% of Telegram, depends only on the above ─────────┐
│  telegram-transport  (Aspire resource; real AddTelegramBot; the ONLY System.Net)    │
│     • boots NO-OP until a token exists (idle, harvested from Projects/ino)           │
│     • inbound:  webhook Update → Send("TelegramMessageReceived", {chatId,text,…})    │
│     • outbound: WatchSynapses(["TelegramReplyRequested","ConfigurationProvided"])    │
│                   → setWebhook + sendMessage                                         │
│  TelegramResponderNeuron  (ships as pack.Code; PURE; rides the broadcast):           │
│     manifest handles "TelegramMessageReceived";                                      │
│     Handle(Signal) → AskLlm{ prompt, replyType:"TelegramReplyRequested", {chatId} }  │
│  manifest.RequiredConfig = [ telegram_token, llm_provider, llm_key ]                 │
│  (typed TelegramMessageReceived/TelegramReplyRequested records are the transport's   │
│   internal representation; they serialize to Signal name+props on the kernel wire)   │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 Why this answers the objections

- **The kernel never "handles" Telegram.** It moves `Signal`s (name+props) and `AskLlm`
  (generic) over a timeline it knows nothing about. `LlmResponderNeuron` echoes a reply
  `Signal` whose name is *passed in* (`ReplyType`) — it never references Telegram.
- **The responder is 100% in the package** (`pack.Code`), pure and sync. It can't call
  `IChatClient` (the contract forbids it), so it emits a generic `AskLlm` intent; the async LLM
  call happens in the generic kernel-resident `LlmResponderNeuron`. `AskLlm` + `Signal` are
  reusable by every future pack.
- **Broadcast is the restored, proven mechanism**, not an invention — `final`'s Orleans
  timeline stream, reflection-only. The N+1 use case falls out for free (a second installed
  pack simply also subscribes).
- **The transport reads its config the same generic way** — it subscribes to the generic
  `ConfigurationProvided` synapse filtered to its own pack.

### 3.2 Distribution principle

A sandboxed pack **declares needs** (required config + the intents it emits) and the
kernel/transports **satisfy** them generically. Telegram is the first capability-transport;
email/HTTP/Discord later are each their own package on the same abstractions. We are **not**
building those now (Musk step 1: no speculative hedges).

### 3.3 Simplification (Musk steps 1–2 — delete before building)

- **Delete** the empty `if(IsBroadcast){}` stub → real timeline broadcast.
- **Do NOT port** `final`'s source-generated handler manifest — reflection-only dispatch
  (proven by `Projects/v3` at 160 LOC). Less machinery, no source generator.
- **Delete** the `AddTelegramBot` `echo` placeholder
  (`DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` ~164–177).
- **Replace** the vague `MarketplaceSeeds.cs` Telegram paragraph with a real signed pack seed.
- Today's `Send` `switch(synapseType)` (`InoRequest`, `LoginRequest`, …) is the same
  hardcoding anti-pattern; the Telegram path must use the new generic `Send`→`Signal` route and
  must not add a case. Fully deleting the switch is out of scope here.

---

## 4. Contracts

### 4.1 Restored broadcast (in `DigitalBrain.Kernel/Neuron.cs` + a `SynapseStream` accessor)

Harvest verbatim-with-rename from `Projects/final/src/DigitalBrain.Os/Infrastructure/Orleans/`:
`SynapseStream` (stream provider `"DigitalBrainTimeline"`, StreamId `timeline:global`),
`SubscribeTimelineIfNeeded` (subscribe iff `HandledTypes(GetType()).Count > 0`, `ResumeAsync`
on reactivation), the `OnNextAsync`→`Receive`→`Dispatch` path, and reflection-based
`HandledTypes`/`Handlers` (drop the manifest branch). Wire the stream provider in
`Program.cs` and `NeuronTestSiloConfigurator` (which already calls `AddMemoryStreams`).

### 4.2 Generic carriers + LLM intent (in `DigitalBrain.Core`, protocol-level)

```csharp
// Pack-defined "types" cross the kernel as a name + prop bag. Base Type == Name so manifest
// matching and reflection dispatch work unchanged.
[GenerateSerializer]
public record Signal(string Name, IReadOnlyDictionary<string, object?> Props)
    : Synapse(Name, DateTimeOffset.UtcNow);

// Generic LLM-intent. A pack returns this; a generic kernel neuron fulfils it and emits a
// Signal whose Name is ReplyType, merging the answer into ReplyProps.
[GenerateSerializer]
public record AskLlm(string Prompt, string ReplyType, IReadOnlyDictionary<string, object?> ReplyProps)
    : Synapse(nameof(AskLlm), DateTimeOffset.UtcNow);
```

### 4.3 Config-prompting primitive (reusable — every future experience inherits it)

`PackManifest` (`DigitalBrain.Core/Distribution/IPackBehavior.cs`) gains:

```csharp
IReadOnlyList<PackConfigField> RequiredConfig { get; }   // default: empty

record PackConfigField(
    string Key, string Label, PackConfigFieldKind Kind,  // Text | Secret | Choice
    IReadOnlyList<string>? Choices = null,
    string? DependsOnKey = null, string? DependsOnValue = null);
```

Generic protocol synapse: `ConfigurationProvided(PackName, Values)` (secret values never
logged). Kernel maps `RequiredConfig` → a `UiWidgetTree` via the existing **ui: kit**
(`Ui.TextField` for Text/Secret, `Ui.Select` for Choice, `Ui.Button` submit;
`DigitalBrain.Core/UiSurfaces.cs`) and broadcasts via `HomeFeedBus`. Submit → `PackConfigStore`
(DataProtection-encrypted, persisted to the Azurite blob already in the AppHost, keyed by
`(scope, pack, key)`).

### 4.4 The responder pack (the only genuinely new behavior — pure, ~15 lines)

```csharp
public sealed class TelegramResponderNeuron : IPackBehavior
{
    public PackManifest GetManifest() => new(
        new[] { new SynapseType("TelegramMessageReceived") },
        RequiredConfig: new[]
        {
            new PackConfigField("telegram_token", "Bot token", PackConfigFieldKind.Secret),
            new PackConfigField("llm_provider", "LLM", PackConfigFieldKind.Choice, new[]{"ollama","openai"}),
            new PackConfigField("llm_key", "API key", PackConfigFieldKind.Secret,
                                DependsOnKey: "llm_provider", DependsOnValue: "openai"),
        });

    public string Respond(string input) => input; // unused path

    public IReadOnlyList<Synapse> Handle(Synapse synapse) =>
        synapse is Signal s && s.Name == "TelegramMessageReceived"
            ? new Synapse[] { new AskLlm(
                Prompt: s.Props.GetValueOrDefault("text")?.ToString() ?? "",
                ReplyType: "TelegramReplyRequested",
                ReplyProps: new Dictionary<string, object?> { ["chatId"] = s.Props.GetValueOrDefault("chatId") }) }
            : Array.Empty<Synapse>();
}
```

> **Note (plan-time):** `PackManifest` currently takes only `HandledSynapseTypes`. Adding the
> optional `RequiredConfig` to its ctor (defaulted) is part of slice "config primitive".

### 4.5 Telegram synapses (in `DigitalBrain.Telegram`, NOT Core)

```csharp
[GenerateSerializer] record TelegramMessageReceived(long ChatId, long FromUserId, string Text, long UpdateId) : Synapse;
[GenerateSerializer] record TelegramReplyRequested(long ChatId, string Text, long? ReplyToMessageId = null) : Synapse;
```

These are the transport's internal representation; on the kernel wire they are `Signal`
name+props. The pack and kernel never see the concrete types.

---

## 5. End-to-end flow (all generic routing; kernel never knows "Telegram")

1. Webhook `Update` → transport builds `TelegramMessageReceived` → `Send("TelegramMessageReceived", {chatId,text,…})`.
2. Kernel `Send` (generic path) → `Signal("TelegramMessageReceived", props)` Emitted onto the timeline (broadcast).
3. Responder's `GeneratedNeuron` (subscribed on activation, embodied at install) receives the Signal → `_embodied.CanHandle` (manifest match) → `Handle(Signal)` → returns `AskLlm{prompt, replyType:"TelegramReplyRequested", {chatId}}` → fired (broadcast).
4. `LlmResponderNeuron : IHandle<AskLlm>` (subscribed) → `await IChatClient…(prompt)` → Emits `Signal("TelegramReplyRequested", {chatId, text:answer})` (broadcast).
5. The egress bus delivers the Signal to the transport's `WatchSynapses(["TelegramReplyRequested"])` gRPC stream → transport maps to `TelegramReplyRequested` → `sendMessage`.

Install/config (also generic): `InstallFromMarketplace` → manifest has `RequiredConfig` → kernel emits config-form `UiSurface` → user submits → `ConfigurationProvided` → `PackConfigStore`; the transport (subscribed to `ConfigurationProvided` for its pack) gets `telegram_token` → `setWebhook` at runtime (no restart); `LlmResponderNeuron`'s `IChatClient` is built from `llm_provider`/`llm_key`.

---

## 6. Verification contract — "tests only, no manual smoke"

Dev loop is 100% green tests, nothing manual:

1. **Broadcast restore (Reqnroll/xUnit over TestCluster), Telegram-free:** two grains both
   `IHandle<TPing>`; one `Emit(TPing)`; assert both react. Then the real N+1: install an echo
   pack whose manifest handles `"Ping"`, broadcast `Signal("Ping",…)`, assert the embodied pack
   reacts — and a *second* installed pack also reacts (0→1→2).
2. **Telegram reactive loop (Reqnroll over TestCluster), written first as the spec:** install →
   assert config-form `UiSurface` has the 3 fields → provide config → assert stored →
   `Send("TelegramMessageReceived",…)` → assert an `AskLlm` is emitted → (stub `IChatClient`) →
   assert a `Signal("TelegramReplyRequested",{chatId,text})` reaches the egress bus.
3. **Transport contract tests:** a deterministic **fake Telegram server** seeded with
   **recorded real webhook payloads**; assert `Update`→`Send` mapping and the exact
   `sendMessage` request shape. This is what makes "no manual smoke" safe.
4. **Flutter tree test:** render the config-form `UiWidgetTree`
   (`app/test/features/experience/experience_hop_view_tree_test.dart` pattern); assert
   token/provider/key fields; simulate submit; assert the action envelope.

**Residual risk (stated, not tested):** real token validity + public webhook reachability only
fail in production. Accepted. LLM is stubbed (deterministic); no live-Ollama E2E in the loop.

---

## 7. Reactive-neuron use cases (all ride the restored broadcast)

1. **Live N+1 (the proof):** install a `KeywordWatcherNeuron` that *also* handles
   `"TelegramMessageReceived"` and turns "remind me…" into a `ReminderScheduled` Signal. Two
   installed packs on one broadcast, no restart.
2. **Reacting to system synapses:** an `InstallAnnouncerNeuron` that `IHandle<NeuroPackInstalled>`
   → `AskLlm`/`Signal("TelegramReplyRequested", …)` — "your brain just learned X".
3. **(stretch) Daily digest:** a timer Signal → summarize recent activity → push to Telegram.

---

## 8. Build order (each slice ends green)

0. **Restore broadcast** — harvest `final`'s timeline subscription + `Emit`/`OnNextAsync`
   (reflection-only) into `Neuron.cs`; wire the stream provider; fill `if(IsBroadcast){}`.
   Prove with the Telegram-free two-grain + N+1 echo-pack test (slice 6 §6.1).
1. **Generic carriers + LLM intent** — `Signal`, `AskLlm`, `LlmResponderNeuron` (stubbed
   `IChatClient` in tests); `GeneratedNeuron` subscribes-always + filters via embodied manifest.
2. **Config primitive** — `RequiredConfig` on `PackManifest`, kernel form emission,
   `PackConfigStore`, `ConfigurationProvided` (Reqnroll + Flutter tree test).
3. **Generic `Send`→`Signal` + `WatchSynapses` egress bus** — switch-free generic `Send`;
   egress bus paralleling `HomeFeedBus`; proven with a fake gRPC subscriber.
4. **Telegram package** — `DigitalBrain.Telegram` typed synapses + `TelegramResponderNeuron`
   pack; the full reactive-loop BDD scenario (§6.2), LLM stubbed.
5. **Transport** — harvest ino webhook/ngrok/`setWebhook`; make `AddTelegramBot` real; contract
   tests vs the fake Telegram server with recorded payloads. **Bot works end-to-end here.**
6. **Reactive use case #1** — the `KeywordWatcherNeuron` N+1 proof scenario.

---

## 9. Decisions locked

- Spine = the Telegram LLM bot experience.
- Packaging = permanent no-op transport (real `AddTelegramBot`) + hot-installed reactive
  responder pack; no restart.
- Broadcast = **restore `final`/`self-improving`'s Orleans timeline stream**, reflection-only
  (drop the source-gen manifest); `GeneratedNeuron` filters via the embodied manifest.
- LLM in a pack = pure pack emits a generic `AskLlm`; generic `LlmResponderNeuron` does the
  async `IChatClient` call; `Signal` carries pack-defined names+props.
- Config = generic pack-declared `RequiredConfig` → in-app form; kernel **DataProtection**
  secret store (over Azurite), keyed by `(scope, pack, key)`.
- LLM default = pluggable, local Ollama.
- Telegram synapse contracts live in `DigitalBrain.Telegram`, cross the wire as named JSON.
- Verification = tests only; recorded-payload fake Telegram server for the API contract.
