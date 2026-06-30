# Telegram LLM Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Telegram bot as an installable, self-configuring marketplace experience that answers messages via an LLM (default local Ollama), with no AppHost restart.

**Architecture:** Restore the regressed Orleans timeline-stream broadcast in the kernel (harvest from `Projects/final`, reflection-only). Add three generic, domain-free primitives — a `Signal` carrier, an `AskLlm` intent + `LlmResponderNeuron`, and a pack config-prompt — then put 100% of Telegram (typed synapses, a pure responder pack, a no-op-until-configured transport harvested from `Projects/ino`) in a `DigitalBrain.Telegram` package that depends only on those generic abstractions. The kernel never references Telegram.

**Tech Stack:** .NET 11, Orleans 10.2 (streams + journaling), Aspire 13.4.6, Microsoft.Extensions.AI (`IChatClient`, Ollama/Azure), Roslyn pack embodiment, Reqnroll BDD over Orleans `TestCluster`, ASP.NET Core (transport), `Telegram.BotAPI` 9.6.0, ASP.NET DataProtection.

## Global Constraints

- **Context7 first.** Before writing any code against Orleans streaming, `Microsoft.Extensions.AI`, ASP.NET DataProtection, `Telegram.BotAPI`, Reqnroll, or gRPC, look the API up via Context7. Training data lags. (User rule.)
- **No vacuous XML docs.** No `/// <summary>` that restates a signature. Self-explanatory names; small inline comments only where genuinely non-obvious. (User rule + CLAUDE.md.)
- **Latest NuGet versions**, pinned in `Directory.Packages.props` (no `Version="*"`).
- **`DigitalBrain.Core` stays pure protocol.** No domain (Telegram) types in Core or Kernel. The kernel must never reference `DigitalBrain.Telegram`.
- **Reflection-only dispatch.** Do NOT port `final`'s source-generated handler manifest.
- **Verify after each task:** `dotnet build` + the task's `dotnet test --filter` must be green before commit. Run `aspire doctor` after AppHost-touching tasks (slice 5).
- **Branch:** work on `spec/telegram-llm-experience` (off `master`). Commit per task.

---

## Slice 0 — Restore the timeline-stream broadcast

Harvest `Projects/final`'s proven mechanism into `brain/`. Sources to read verbatim and adapt (rename namespaces to `DigitalBrain.Kernel`):
`Projects/final/src/DigitalBrain.Os/Infrastructure/Orleans/SynapseStream.cs`,
`…/Neuron.cs` (`SubscribeTimelineIfNeeded` lines ~56-76, `OnActivateAsync` ~122-150, `Emit`/`Fire` ~235-276, `OnNextAsync`/`Receive`/`Dispatch` ~169-233),
`…/SynapseDispatch.cs` (keep ONLY the reflection branch `Handlers`/`HandledTypes` — drop `GetManifestIfAvailable`/source-gen).

### Task 0.1: Add the timeline stream accessor + register the provider

**Files:**
- Create: `brain/DigitalBrain.Kernel/SynapseStream.cs`
- Modify: `brain/DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs` (already calls `AddMemoryStreams("Default")`/`("HomeFeed")` — add the timeline provider)
- Modify: `brain/DigitalBrain.Kernel/.../Program.cs` (silo builder — add `AddMemoryStreams("DigitalBrainTimeline")`)
- Test: `brain/DigitalBrain.Tests/Kernel/TimelineStreamTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.Kernel.SynapseStream.ProviderName` (const `"DigitalBrainTimeline"`); extension `IStreamProvider.Timeline() → IAsyncStream<Synapse>` (StreamId `("timeline","global")`).

- [ ] **Step 1: Write `SynapseStream.cs`** (adapt from final verbatim)

```csharp
using DigitalBrain.Core;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

public static class SynapseStream
{
    public const string ProviderName = "DigitalBrainTimeline";

    public static IAsyncStream<Synapse> Timeline(this IStreamProvider provider) =>
        provider.GetStream<Synapse>(StreamId.Create("timeline", "global"));
}
```

- [ ] **Step 2: Register the provider in the test silo** — add `.AddMemoryStreams("DigitalBrainTimeline")` to `NeuronTestSiloConfigurator.Configure` next to the existing `AddMemoryStreams` calls (PubSubStore is already present).

- [ ] **Step 3: Register in the real silo** — add `.AddMemoryStreams("DigitalBrainTimeline")` to the silo builder in the kernel `Program.cs` (Context7-verify the Orleans 10.2 `AddMemoryStreams` signature first).

- [ ] **Step 4: Write a failing smoke test** in `TimelineStreamTests.cs`: get the stream provider from `cluster.Client` (or a grain), call `.Timeline()`, assert non-null. Run:
`dotnet test brain/DigitalBrain.Tests --filter "FullyQualifiedName~TimelineStreamTests"` → expect FAIL then PASS after Steps 1-3.

- [ ] **Step 5: Commit** — `git commit -m "feat(kernel): add DigitalBrainTimeline stream accessor + provider"`

### Task 0.2: Neuron base subscribes on activation + broadcasts + dispatches

**Files:**
- Modify: `brain/DigitalBrain.Kernel/Neuron.cs` (the `FireAsync` empty `if (stamped.IsBroadcast){}`, `OnActivateAsync`, add `IAsyncObserver<Synapse>` impl + dispatch)
- Create: `brain/DigitalBrain.Kernel/SynapseDispatch.cs` (reflection-only, from final minus manifest)
- Test: `brain/DigitalBrain.Tests/Kernel/BroadcastReactivityTests.cs`

**Interfaces:**
- Consumes: `IHandle<T>` = `Task HandleAsync(T synapse)` (`DigitalBrain.Core/IHandle.cs`), `Synapse.IsBroadcast`/`Stamp` (`DigitalBrain.Core/Synapse.cs`).
- Produces: `Neuron` now implements `IAsyncObserver<Synapse>`; broadcasts via `Timeline().OnNextAsync`; a `protected Task Broadcast(Synapse)` helper. `SynapseDispatch.HandledTypes(Type) → IReadOnlySet<Type>`, `SynapseDispatch.DispatchAsync(host, logger, self, synapse) → Task`.

- [ ] **Step 1: Write the failing test** — two minimal grains both `IHandle<Ping>` (define `record Ping(...) : Synapse` as a test type); activate both; one `Broadcast(new Ping(...))`; assert both recorded receipt.

```csharp
[Fact]
public async Task Broadcast_reaches_every_activated_handler()
{
    var builder = new TestClusterBuilder();
    builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
    var cluster = builder.Build();
    await cluster.DeployAsync();
    try
    {
        var a = cluster.GrainFactory.GetGrain<IPingSink>("a");
        var b = cluster.GrainFactory.GetGrain<IPingSink>("b");
        await a.EnsureActiveAsync();   // forces OnActivate → subscribe
        await b.EnsureActiveAsync();
        var emitter = cluster.GrainFactory.GetGrain<IPingEmitter>("e");
        await emitter.EmitPingAsync("hello");
        await Task.Delay(250);
        Assert.Equal(1, await a.ReceivedCountAsync());
        Assert.Equal(1, await b.ReceivedCountAsync());
    }
    finally { await cluster.StopAllSilosAsync(); }
}
```
(Define `IPingSink`/`IPingEmitter` test grains in the test project; `PingSink : Neuron, IHandle<Ping>` increments a counter in `HandleAsync`.)

- [ ] **Step 2: Run it — expect FAIL** (broadcast is the empty stub). `dotnet test brain/DigitalBrain.Tests --filter "FullyQualifiedName~BroadcastReactivityTests"`.

- [ ] **Step 3: Add `SynapseDispatch.cs`** — reflection-only. Adapt from final's `SynapseDispatch.cs` keeping only:

```csharp
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel;

internal static class SynapseDispatch
{
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<Type, MethodInfo>> HandlerCache = new();
    private static readonly ConcurrentDictionary<Type, FrozenSet<Type>> HandledTypesCache = new();

    public static IReadOnlySet<Type> HandledTypes(Type neuronType)
    {
        _ = Handlers(neuronType);
        return HandledTypesCache[neuronType];
    }

    public static Task DispatchAsync(object host, ILogger logger, object self, Synapse synapse)
    {
        var handlers = Handlers(host.GetType());
        if (handlers.TryGetValue(synapse.GetType(), out var method))
            return (Task)method.Invoke(host, [synapse])!;
        logger.LogWarning("{Neuron}: no handler for {Synapse}", self, synapse.GetType().Name);
        return Task.CompletedTask;
    }

    private static FrozenDictionary<Type, MethodInfo> Handlers(Type neuronType) =>
        HandlerCache.GetOrAdd(neuronType, static t =>
        {
            var map = new Dictionary<Type, MethodInfo>();
            foreach (var i in t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandle<>)))
            {
                var st = i.GetGenericArguments()[0];
                map[st] = i.GetMethod(nameof(IHandle<Synapse>.HandleAsync))!;
            }
            var fd = map.ToFrozenDictionary();
            HandledTypesCache[t] = fd.Keys.ToFrozenSet();
            return fd;
        });
}
```
(Confirm the real `IHandle<T>` method name/arity — extraction showed `Task HandleAsync(T synapse)`; the invoke passes `[synapse]` only.)

- [ ] **Step 4: Make `Neuron` subscribe + broadcast + dispatch.** In `Neuron.cs`:
  - Implement `IAsyncObserver<Synapse>` (`OnNextAsync`, `OnCompletedAsync`, `OnErrorAsync`).
  - In `OnActivateAsync`, after existing setup, add: `if (SynapseDispatch.HandledTypes(GetType()).Count > 0) { subscribe-or-resume to GetStreamProvider(SynapseStream.ProviderName).Timeline(); }` — adapt final's `SubscribeTimelineIfNeeded` (GetAllSubscriptionHandles → ResumeAsync else SubscribeAsync). Store the handle.
  - `OnNextAsync(item)`: `SynapseDispatch.HandledTypes(GetType()).Contains(item.GetType()) ? DispatchAsync(this, Logger, Self, item) : Task.CompletedTask`.
  - Fill the empty broadcast block in `FireAsync`: `await GetStreamProvider(SynapseStream.ProviderName).Timeline().OnNextAsync(stamped);`. Add a `protected Task Broadcast(Synapse s)` that stamps `s with { IsBroadcast = true }` and calls `FireAsync`.
  - Context7-verify Orleans 10.2 `SubscribeAsync(IAsyncObserver<T>)` / `StreamSubscriptionHandle.ResumeAsync` before writing.

- [ ] **Step 5: Run the test — expect PASS.** Same filter as Step 2.

- [ ] **Step 6: Full kernel build + existing tests green** — `dotnet build brain/Brain.slnx` then `dotnet test brain/DigitalBrain.Tests --filter "FullyQualifiedName~Kernel"`. Fix any point-to-point delivery regressions (the non-broadcast branches of `FireAsync` must be unchanged).

- [ ] **Step 7: Commit** — `git commit -m "feat(kernel): restore timeline broadcast + reflection dispatch (harvest Projects/final)"`

---

## Slice 1 — Generic carriers (`Signal`, `AskLlm`) + `LlmResponderNeuron` + dynamic `GeneratedNeuron`

### Task 1.1: `Signal` and `AskLlm` synapses

**Files:** Create `brain/DigitalBrain.Core/Signals.cs`. Test: `brain/DigitalBrain.Tests/Kernel/SignalTests.cs`.

**Interfaces:** Produces `Signal(string Name, IReadOnlyDictionary<string,object?> Props) : Synapse` (base `Type == Name`); `AskLlm(string Prompt, string ReplyType, IReadOnlyDictionary<string,object?> ReplyProps) : Synapse`.

- [ ] **Step 1:** Write a failing test: `new Signal("X", new Dictionary<string,object?>{["k"]=1}).Type == "X"` and round-trips through Orleans serialization (use the cluster's serializer or assert `Type`).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Write `Signals.cs` exactly as spec §4.2 (both records, `[GenerateSerializer]`, derive from `Synapse(Name/nameof, DateTimeOffset.UtcNow)`).
- [ ] **Step 4:** Run → PASS. `dotnet test brain/DigitalBrain.Tests --filter "FullyQualifiedName~SignalTests"`.
- [ ] **Step 5:** Commit — `feat(core): Signal carrier + AskLlm intent`.

### Task 1.2: `LlmResponderNeuron` (generic, async LLM)

**Files:** Create `brain/DigitalBrain.Kernel/LlmResponderNeuron.cs`. Test: `brain/DigitalBrain.Tests/Kernel/LlmResponderTests.cs`. Register the grain interface alongside other system neurons.

**Interfaces:**
- Consumes: `AskLlm` (1.1); `IChatClient` via `ServiceProvider.GetService<IChatClient>()` (pattern from `GeneratedNeuron.UseExperienceAsync`); `Broadcast` (0.2).
- Produces: `ILlmResponderNeuron : INeuron, IHandle<AskLlm>`; emits `Signal(askLlm.ReplyType, askLlm.ReplyProps + {["text"]=answer})`.

- [ ] **Step 1:** Register a **fake `IChatClient`** in the test silo (deterministic: returns `"ANSWER:"+prompt`). Add to a test-only silo configurator variant or extend `NeuronTestSiloConfigurator` to register a fake when none present.
- [ ] **Step 2:** Write failing test: activate `LlmResponderNeuron`; `Broadcast(new AskLlm("hi","ReplyX",{["chatId"]=7}))` from a helper; assert a `Signal` named `"ReplyX"` with `Props["chatId"]==7` and `Props["text"]=="ANSWER:hi"` appears on the responder's timeline/journal.
- [ ] **Step 3:** Run → FAIL.
- [ ] **Step 4:** Implement `LlmResponderNeuron : Neuron, ILlmResponderNeuron`:

```csharp
public async Task HandleAsync(AskLlm ask)
{
    var chat = ServiceProvider.GetService<IChatClient>();
    var text = chat is null ? "[no-llm]" : (await chat.GetResponseAsync(ask.Prompt)).Text.Trim();
    var props = new Dictionary<string, object?>(ask.ReplyProps) { ["text"] = text };
    await Broadcast(new Signal(ask.ReplyType, props));
}
```
(Context7-verify the `Microsoft.Extensions.AI` `IChatClient.GetResponseAsync` shape — extraction showed `.Text`.)

- [ ] **Step 5:** Run → PASS. `--filter "FullyQualifiedName~LlmResponderTests"`.
- [ ] **Step 6:** Commit — `feat(kernel): generic LlmResponderNeuron (AskLlm → IChatClient → reply Signal)`.

### Task 1.3: `GeneratedNeuron` rides the broadcast

**Files:** Modify `brain/DigitalBrain.Kernel/SystemNeurons.cs` (`GeneratedNeuron`). Test: `brain/DigitalBrain.Tests/Distribution/PackBroadcastReactivityTests.cs`.

**Interfaces:**
- Consumes: restored broadcast (0.2), `_embodied.CanHandle`/`.Handle` (`EmbodiedPack`), `PackManifest.HandledSynapseTypes`.
- Produces: `GeneratedNeuron` subscribes to the timeline **unconditionally** (it is a dynamic handler) and, on each broadcast `Signal`, if `_embodied?.CanHandle(signal) == true`, runs `_embodied.Handle(signal)` and broadcasts each output.

- [ ] **Step 1:** Write failing test (the real N+1 on a broadcast): publish+install an echo pack whose `GetManifest` handles `"Ping"` and whose `Handle(Signal s)` returns `[new PackEmission(...)]` when `s.Name=="Ping"`; `Broadcast(new Signal("Ping",…))`; assert the embodied pack reacted (1 `PackEmission`). Then install a **second** pack handling `"Ping"`; broadcast again; assert **2** reactions (0→1→2). Mirror `HandlerGrowthTests` patterns.
- [ ] **Step 2:** Run → FAIL (GeneratedNeuron doesn't subscribe to broadcasts; only receives point-to-point).
- [ ] **Step 3:** In `GeneratedNeuron`: override the subscription so it always subscribes to the timeline on activate (don't gate on static `HandledTypes`, which is empty for the generic host). In `OnNextAsync`/dispatch, before the existing `switch`, add: `if (await TryDispatchEmbodiedAsync(synapse)) return;` for broadcast Signals (the method already exists and checks `_embodied.CanHandle`). Ensure `EnsureEmbodied()` re-embodies from the journaled `NeuroPackInstalled` after reactivation.
- [ ] **Step 4:** Run → PASS. `--filter "FullyQualifiedName~PackBroadcastReactivityTests"`.
- [ ] **Step 5:** Commit — `feat(kernel): GeneratedNeuron reacts to broadcast via embodied manifest`.

---

## Slice 2 — Config-prompting primitive

### Task 2.1: `RequiredConfig` on the manifest + `ConfigurationProvided`

**Files:** Modify `brain/DigitalBrain.Core/Distribution/IPackBehavior.cs` (extend `PackManifest`, add `PackConfigField`/`PackConfigFieldKind`); create `brain/DigitalBrain.Core/Configuration.cs` (`ConfigurationProvided`). Test: `brain/DigitalBrain.Tests/Distribution/PackConfigManifestTests.cs`.

**Interfaces:** Produces `PackManifest(IReadOnlyList<SynapseType> HandledSynapseTypes, IReadOnlyList<PackConfigField>? RequiredConfig = null)`; `PackConfigField` per spec §4.3; `ConfigurationProvided(string PackName, IReadOnlyDictionary<string,string> Values) : Synapse`.

- [ ] **Step 1:** Failing test: a pack returning a manifest with `RequiredConfig` exposes those fields; default manifest has empty `RequiredConfig`. Keep the existing `GetManifest()` default (handles `ExperienceUsed`) working.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Extend `PackManifest` ctor with the optional `RequiredConfig` (default empty list so all existing packs/tests compile). Add the records.
- [ ] **Step 4:** Run → PASS; also run the full distribution test suite to confirm no break from the manifest change. `--filter "FullyQualifiedName~Distribution"`.
- [ ] **Step 5:** Commit — `feat(core): pack RequiredConfig manifest + ConfigurationProvided`.

### Task 2.2: `PackConfigStore` (DataProtection over Azurite)

**Files:** Create `brain/DigitalBrain.Kernel/Config/PackConfigStore.cs` + DI registration in `Program.cs`. Test: `brain/DigitalBrain.Tests/Kernel/PackConfigStoreTests.cs`.

**Interfaces:** Produces `IPackConfigStore { Task SetAsync(string scope, string pack, IReadOnlyDictionary<string,string> values); Task<IReadOnlyDictionary<string,string>> GetAsync(string scope, string pack); }`. Values encrypted via `IDataProtector`; persisted to the Azurite blob container already in the AppHost (in tests: an in-memory backing).

- [ ] **Step 1:** Context7: ASP.NET DataProtection (`IDataProtectionProvider.CreateProtector`, `Protect`/`Unprotect`) and the Azure Blob persistence option.
- [ ] **Step 2:** Failing test (in-memory backing): `SetAsync("s","telegram",{token:"abc"})` then `GetAsync` returns `{token:"abc"}`; assert the persisted bytes are NOT plaintext `"abc"`.
- [ ] **Step 3:** Run → FAIL.
- [ ] **Step 4:** Implement `PackConfigStore` keyed `(scope,pack,key)`, encrypt per value, store via an injected backing store (blob in prod, in-memory in tests). Register `IPackConfigStore` + DataProtection in `Program.cs` and a test double in the test silo.
- [ ] **Step 5:** Run → PASS. `--filter "FullyQualifiedName~PackConfigStoreTests"`.
- [ ] **Step 6:** Commit — `feat(kernel): DataProtection-encrypted PackConfigStore`.

### Task 2.3: Kernel renders the config form on install; submit stores it

**Files:** Modify `brain/DigitalBrain.Kernel/SystemNeurons.cs` (`MarketplaceNeuron.HandleAsync(InstallFromMarketplace)` — after `NeuroPackInstalled`, if the embodied manifest has `RequiredConfig`, build + broadcast a config-form `UiSurface`); modify `GatewayService.Send` to handle `ConfigurationProvided` → `PackConfigStore`. Test: `brain/DigitalBrain.Tests/Distribution/ConfigFormFlowTests.cs`.

**Interfaces:**
- Consumes: `RequiredConfig` (2.1), `PackConfigStore` (2.2), `UiWidgetTree`/`Ui.*` (`DigitalBrain.Core/UiSurfaces.cs`), `HomeFeedBus.Broadcast`.
- Produces: a `UiSurface` whose tree contains a `Ui.TextField`/`Ui.Select`/`Ui.Button` per field; `ConfigurationProvided` handling in `Send`.

- [ ] **Step 1:** Failing Reqnroll scenario (`Features/TelegramExperience.feature` + steps): install a pack with 3-field `RequiredConfig`; assert a config-form `UiSurface` is emitted whose tree has fields `telegram_token`, `llm_provider`, `llm_key`. Then send `ConfigurationProvided`; assert `PackConfigStore.GetAsync` returns the values.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement: a `BuildConfigForm(manifest.RequiredConfig)` mapping fields → `UiWidgetTree` (Text/Secret→`Ui.TextField` with `["secret"]=true`, Choice→`Ui.Select` with `["items"]=choices`, plus a submit `Ui.Button` carrying `synapseType="ConfigurationProvided"` + `pack`). Emit via `FireAsync` + `HomeFeedBus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(...))`. Add a `ConfigurationProvided` branch to `GatewayService.Send` that deserializes props and calls `PackConfigStore.SetAsync`.
- [ ] **Step 4:** Run → PASS. `--filter "FullyQualifiedName~ConfigFormFlowTests"`.
- [ ] **Step 5:** Commit — `feat(kernel): emit pack config form on install; persist ConfigurationProvided`.

### Task 2.4: Flutter tree test for the config form

**Files:** Test: `app/test/features/experience/config_form_tree_test.dart` (mirror `experience_hop_view_tree_test.dart`).

- [ ] **Step 1:** Write a tree test: feed a config-form `UiWidgetTree` JSON (token `ui:TextField`, provider `ui:Select`, key `ui:TextField`, submit `ui:Button`) to `UiSurfaceTreeRenderer`/registry; assert the three fields + button render; simulate submit; assert the action envelope carries `synapseType: "ConfigurationProvided"` + the field values.
- [ ] **Step 2:** Run → adjust until green: `flutter test test/features/experience/config_form_tree_test.dart` (from `app/`).
- [ ] **Step 3:** Commit (in `app/` repo) — `test(experience): config-form tree renders + submits`.

---

## Slice 3 — Generic `Send`→`Signal` + `WatchSynapses` egress bus

### Task 3.1: Generic `Send` fallback creates a `Signal` broadcast

**Files:** Modify `brain/DigitalBrain.Kernel/Gateway/GatewayService.cs` (replace the `throw` fallback in `Send` with: unknown `type_name` → build `Signal(type_name, propsFromJson)` and broadcast it onto the timeline via a generic ingress). Add a tiny `IngressNeuron` if needed so the Signal is stamped/journaled. Test: `brain/DigitalBrain.Tests/Gateway/GenericSendTests.cs`.

**Interfaces:** Consumes `SynapseEnvelope{correlation_id,type_name,payload}` (proto), `Signal`, broadcast. Produces: any non-built-in `Send` becomes a broadcast `Signal`.

- [ ] **Step 1:** Failing test: a grain `IHandle<Signal>`-equivalent (or the responder) reacts after `Send(type_name:"TelegramMessageReceived", payload: {"chatId":7,"text":"hi"})`; assert a `Signal("TelegramMessageReceived", {chatId:7,text:"hi"})` was broadcast. Use the gateway service directly or a thin client.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement the generic fallback: parse `payload` JSON → `Dictionary<string,object?>`; get an `IngressNeuron` grain (keyed by correlation) and have it `Broadcast(new Signal(request.TypeName, props))`. Must NOT add a Telegram-specific case; must come after the existing built-in cases but replace the final `throw`.
- [ ] **Step 4:** Run → PASS. `--filter "FullyQualifiedName~GenericSendTests"`.
- [ ] **Step 5:** Commit — `feat(gateway): generic Send → Signal broadcast (no per-type switch)`.

### Task 3.2: `WatchSynapses` egress bus + RPC

**Files:** Create `brain/DigitalBrain.Kernel/Ui/SignalEgressBus.cs` (parallel to `HomeFeedBus`); add `WatchSynapses(WatchSynapsesRequest) returns (stream SynapseEnvelope)` to the proto + `GatewayService`; a silo-side stream subscriber feeding the bus. Test: `brain/DigitalBrain.Tests/Gateway/WatchSynapsesTests.cs`.

**Interfaces:**
- Consumes: timeline stream, `Signal`.
- Produces: `WatchSynapses(typeFilter[])` server-stream that emits `SynapseEnvelope{type_name, payload}` for each broadcast `Signal` whose `Name` ∈ filter. `SignalEgressBus.Subscribe(filter)`.

- [ ] **Step 1:** Context7: Orleans implicit/explicit stream subscription on the silo (a `[ImplicitStreamSubscription]` consumer or an `IStreamProvider` subscription in a hosted service) and gRPC server-streaming.
- [ ] **Step 2:** Failing test: subscribe to the egress bus with filter `["TelegramReplyRequested"]`; broadcast `Signal("TelegramReplyRequested",{chatId:7,text:"yo"})` and `Signal("Other",{})`; assert only the first is delivered with correct props.
- [ ] **Step 3:** Run → FAIL.
- [ ] **Step 4:** Implement `SignalEgressBus` (channel-per-subscriber + content-hash dedup, copy `HomeFeedBus` shape) fed by a silo-side timeline subscriber that forwards `Signal`s. Add the `WatchSynapses` RPC streaming filtered envelopes. (Egress bus carries only `Signal`s, so external clients never need CLR types.)
- [ ] **Step 5:** Run → PASS. `--filter "FullyQualifiedName~WatchSynapsesTests"`.
- [ ] **Step 6:** Commit — `feat(gateway): WatchSynapses egress bus for external transports`.

---

## Slice 4 — `DigitalBrain.Telegram`: typed synapses + responder pack

### Task 4.1: New project + typed synapses + responder pack seed

**Files:** Create project `brain/DigitalBrain.Telegram/DigitalBrain.Telegram.csproj` (refs `DigitalBrain.Core` only); `Synapses.cs` (spec §4.5); `TelegramResponderNeuron.cs` (spec §4.4, as `pack.Code` text + a compiled copy for unit testing); add to `Brain.slnx`. Test: `brain/DigitalBrain.Tests/Telegram/ResponderPackTests.cs`.

**Interfaces:** Produces the responder pack source string (the marketplace `Code`) and the `TelegramMessageReceived`/`TelegramReplyRequested` typed records (transport-internal).

- [ ] **Step 1:** Failing unit test: instantiate `TelegramResponderNeuron`; `GetManifest()` handles `"TelegramMessageReceived"` and has the 3 `RequiredConfig` fields; `Handle(new Signal("TelegramMessageReceived", {chatId:7,text:"hi"}))` returns one `AskLlm` with `Prompt=="hi"`, `ReplyType=="TelegramReplyRequested"`, `ReplyProps["chatId"]==7`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Write `TelegramResponderNeuron` exactly as spec §4.4. Add the typed synapses. Create the `csproj` (Context7-verify nothing extra needed; Core ref only — it must pass `CapabilityGate` when compiled as a pack: no `System.Net`).
- [ ] **Step 4:** Run → PASS. `--filter "FullyQualifiedName~ResponderPackTests"`.
- [ ] **Step 5:** Replace the `MarketplaceSeeds.cs` Telegram paragraph with a real seed whose `Code` is the responder source (signed per existing `PackSignatureVerifier` flow used by other seeds). Verify embodiment: a test that publishes+installs the seed and confirms `_embodied` is non-null (passes `CapabilityGate`).
- [ ] **Step 6:** Commit — `feat(telegram): DigitalBrain.Telegram synapses + responder pack + seed`.

### Task 4.2: Full reactive-loop BDD scenario (LLM stubbed)

**Files:** `brain/DigitalBrain.Tests/Features/TelegramExperience.feature` (+ steps). Uses the fake `IChatClient` and the egress bus.

- [ ] **Step 1:** Write the scenario: install the Telegram experience → config form has 3 fields → provide config → `Send("TelegramMessageReceived",{chatId,text})` → assert `AskLlm` emitted → assert `Signal("TelegramReplyRequested",{chatId,text:"ANSWER:hi"})` reaches the egress bus subscriber filtered on `"TelegramReplyRequested"`.
- [ ] **Step 2:** Run → iterate to green. `--filter "FullyQualifiedName~TelegramExperience"`.
- [ ] **Step 3:** Commit — `test(telegram): end-to-end reactive loop over TestCluster (LLM stubbed)`.

---

## Slice 5 — Transport (harvest ino) + real `AddTelegramBot`. **Bot works end-to-end.**

### Task 5.1: Transport host project (no-op until token)

**Files:** Create `brain/DigitalBrain.Telegram.Transport/` (ASP.NET Core): `TelegramBotOptions.cs`, `WebhookSetupService.cs`, `Program.cs` — harvest verbatim from `Projects/ino/clients/Telegram/` then rewire. Add `Telegram.BotAPI` (latest; ino used 9.6.0) to `Directory.Packages.props`. Test: `brain/DigitalBrain.Tests/Telegram/TransportContractTests.cs`.

**Interfaces:**
- Consumes: brain gRPC `Send` + `WatchSynapses` (3.1/3.2); `ConfigurationProvided` egress for its token.
- Produces: webhook `Update` → `Send("TelegramMessageReceived", props)`; `WatchSynapses(["TelegramReplyRequested","ConfigurationProvided"])` → `setWebhook`/`sendMessage`.

- [ ] **Step 1:** Context7: `Telegram.BotAPI` 9.6.0 — `SetWebhookAsync`, webhook `Update` deserialization, `SendMessageAsync` (extraction has ino's exact usage to mirror).
- [ ] **Step 2:** Harvest `TelegramBotOptions.cs` + `WebhookSetupService.cs` verbatim (optional-token boot + ngrok resolution + `setWebhook`). In `Program.cs`, keep the `/webhook` endpoint + optional secret-token check; **replace** ino's `InoClient.Chat` gRPC with the brain `DigitalBrainGateway` client: map `Update` → `Send("TelegramMessageReceived", {chatId,fromUserId,text,updateId})`.
- [ ] **Step 3:** Add a `WatchSynapses` consumer: on `ConfigurationProvided` for this pack → set token + `setWebhook`; on `TelegramReplyRequested` → `sendMessage(chatId, text)`.
- [ ] **Step 4 (contract test):** stand up a **fake Telegram server** (an in-proc `HttpMessageHandler`/test server) seeded with a recorded `Update` JSON payload; POST it to `/webhook`; assert the transport calls brain `Send` with the exact mapped props. Then push a `Signal("TelegramReplyRequested",…)` through the `WatchSynapses` consumer; assert the exact `sendMessage` request body. Run: `dotnet test brain/DigitalBrain.Tests --filter "FullyQualifiedName~TransportContractTests"`.
- [ ] **Step 5:** Commit — `feat(telegram): transport host (harvest ino) — webhook ↔ Send/WatchSynapses`.

### Task 5.2: Make `AddTelegramBot` real + wire AppHost

**Files:** Modify `brain/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (`AddTelegramBot` → `AddProject<DigitalBrain.Telegram.Transport>` with `WithReference(kernel)` + the gRPC endpoint env + optional `Telegram__BotToken` param); confirm `NeuroOSPrototype.AppHost/AppHost.cs` `DIGITALBRAIN_ENABLE_TELEGRAM` path and `brain.cs --telegram`.

- [ ] **Step 1:** Replace the `AddExecutable("echo", …)` body with a real `AddProject`/`AddExecutable` pointing at the transport, referencing the kernel and injecting the gateway endpoint. Context7: Aspire 13.4.6 `AddProject` + `WithReference` + parameter resources.
- [ ] **Step 2:** Make the transport **always present but no-op** (boots without a token), per spec — so it needs no restart when configured later. (Keep it behind `DIGITALBRAIN_ENABLE_TELEGRAM` only if you want it off by default; default-on no-op is the spec intent — decide and note.)
- [ ] **Step 3:** Verify hosting: `aspire doctor` (via aspire MCP `doctor`), then `dotnet build brain/Brain.slnx`. Do NOT require a live `aspire run` in CI; a build + `doctor` is the gate.
- [ ] **Step 4:** Commit — `feat(aspire): real AddTelegramBot transport resource (delete echo stub)`.

---

## Slice 6 — Reactive use case #1: `KeywordWatcherNeuron` (N+1 proof)

**Files:** Add a second pack seed `KeywordWatcherNeuron` (handles `"TelegramMessageReceived"`, turns `"remind me …"` into a `ReminderScheduled` Signal) + `ReminderScheduled` is just a `Signal` name. Test: extend `TelegramExperience.feature`.

- [ ] **Step 1:** Failing scenario: with the responder already installed, install `KeywordWatcher`; `Send("TelegramMessageReceived",{text:"remind me to call mom"})`; assert **both** an `AskLlm` (from responder) **and** a `Signal("ReminderScheduled",…)` (from watcher) appear — two installed packs reacting to one broadcast, no restart.
- [ ] **Step 2:** Run → FAIL (only responder installed initially).
- [ ] **Step 3:** Add the `KeywordWatcherNeuron` pack source + seed (pure `IPackBehavior`, `Handle(Signal)` returns the `ReminderScheduled` Signal when text starts with "remind me").
- [ ] **Step 4:** Run → PASS. `--filter "FullyQualifiedName~TelegramExperience"`.
- [ ] **Step 5:** Commit — `feat(telegram): KeywordWatcher pack — N+1 reactivity proof`.

---

## Final verification

- [ ] `dotnet build brain/Brain.slnx` clean.
- [ ] `dotnet test brain/DigitalBrain.Tests` — fast suite green (broadcast, signals, config, telegram loop, transport contract, N+1).
- [ ] `flutter test` in `app/` — config-form tree test green.
- [ ] `aspire doctor` clean (transport resource wired).
- [ ] Code review (the user's standing rule: run review before returning results) over the diff: Core/Kernel contain zero Telegram references; no `Version="*"`; no vacuous XML docs; names self-explanatory.
