# Reactive Neurons and AI Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `ScheduleTurn` workaround with a durable inbox drain in the kernel, then bring the AI module back in the three-project shape with an `agent` neuron (MAF `ChatClientAgent` with the four brain operations as tools) and a `chat` neuron (MAF group chat over real agent neurons).

**Architecture:** `Deliver` accepts only; the base `Neuron` drains its incoming journal in its own turns against a durable cursor, so a reaction may fire back at its source. The AI module is `DigitalBrain.Modules.AI.Contracts` (names and documented body shapes), `DigitalBrain.Modules.AI` (`AgentNeuron`, `ChatNeuron`, `AIModule` with one `IChatClient` per provider), and `DigitalBrain.Modules.AI.Aspire.Hosting` (`AddAI()` for the AppHost). MAF is the engine; the graph is the truth.

**Tech Stack:** .NET 11 preview, Orleans 10.2.2 (+ Journaling), Microsoft.Agents.AI 1.19.0, Microsoft.Agents.AI.Workflows 1.19.0, Microsoft.Extensions.AI 10.9.0 (+ .OpenAI), Reqnroll 3.3.4 / xunit v3, Aspire 13.5.

**Spec:** `docs/architecture/05-ai-module.md` (binding), with `01-communication-model.md`, `03-mcp-surface.md`, `04-testing-and-projects.md`.

## Global Constraints

- Inside a turn a neuron may fire and read, never wait. No timers or blocking waits inside `ReceiveAsync`.
- Every accepted signal is reacted to exactly once across restarts, in journal order per neuron; a throwing reaction keeps the cursor and is retried on the next wake.
- `Fire` returns once every target has accepted. `FireOutcome.Delivered` = accepted count.
- The 64 KB body cap and the 256-type cap stay. Vocabulary is letters only.
- Module vocabulary is exactly `Instruct`, `Ask`, `Reply`, `Turn`, `Said`. No `ToolCall`, `Choice`, `Show`.
- An agent's tools are `fire`, `connect`, `disconnect`, `read` bound to its own neuron, plus native tools enabled by name in `Instruct.tools`.
- Nothing under `src/Aspire` is edited by hand except `AppHost.cs` calling the AI module's own hosting extension (one line) and the AppHost csproj referencing that hosting project.
- Warnings-as-errors with preview-all analyzers; `dotnet build DigitalBrain.slnx` clean and `dotnet test tests/DigitalBrain.Tests` green at the end of every task.
- Commit trailer on every commit:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BsRRKgTHxZbtHL94dVL9e1
  ```

---

## File map

**Kernel (`src/Kernel/DigitalBrain`)**
| File | Change |
|---|---|
| `Neuron/Neuron.cs` | `Deliver` accepts only; `Drain` one-way self-call; cursor; `ScheduleTurn` removed |
| `Neuron/NeuronRuntime.cs` | binds `IDurableValue<long>` keyed `"reacted"` |
| `Neuron/NeuronRequestPath.cs` | deleted |
| `Neuron/JournalWindow.cs` | adds `TryRead(long sequence, out SignalDelivery)` |
| `Neuron/NeuronJournals.cs` | exposes `IncomingLastSequence`, `TryReadIncoming` |
| `../DigitalBrain.Contracts/Neurons/INeuronDrain.cs` | new: `[OneWay] Task Drain()` |

**Module**
| File | Responsibility |
|---|---|
| `src/Modules/AI/Contracts/DigitalBrain.Modules.AI.Contracts.csproj` | no dependencies beyond Contracts |
| `src/Modules/AI/Contracts/AIVocabulary.cs` | grain type names, signal type names, body shape docs |
| `src/Modules/AI/AI/DigitalBrain.Modules.AI.csproj` | MAF + Extensions.AI packages |
| `src/Modules/AI/AI/AIModule.cs` | `IModule`: keyed `IChatClient` per provider, default provider, native tool registry |
| `src/Modules/AI/AI/Providers.cs` | provider resolution from configuration |
| `src/Modules/AI/AI/BrainTools.cs` | the four operations as `AIFunction`s bound to one neuron |
| `src/Modules/AI/AI/AgentNeuron.cs` | `Neuron<AgentState>` |
| `src/Modules/AI/AI/AgentState.cs` | sessions by correlation, bounded |
| `src/Modules/AI/AI/ChatNeuron.cs` | `Neuron<ChatState>` |
| `src/Modules/AI/AI/ChatState.cs` | checkpoint per correlation, asker, participants |
| `src/Modules/AI/AI/Bodies.cs` | JSON read/write for the five words |
| `src/Modules/AI/Aspire.Hosting/DigitalBrain.Modules.AI.Aspire.Hosting.csproj` | references Aspire.Hosting + module |
| `src/Modules/AI/Aspire.Hosting/AIHostingExtensions.cs` | `AddAI(this DigitalBrainBuilder, ...)` |

**Tests (`tests/DigitalBrain.Tests/Features`)**: `react.feature` + `ReactSteps.cs` + reactive fixtures in `Fixtures.cs`; `agent.feature`, `chat.feature` + `AiSteps.cs`; `ScriptedChatClient.cs` extended for tool calls.

---

### Task 1: Revert the kernel and doc parts of `7e55ef26`, keep doc 05

**Files:**
- Revert: everything `7e55ef26` touched except `docs/architecture/05-ai-module.md` (not in that commit)
- Preserve: uncommitted edits to `src/Modules/AI/DigitalBrain.Modules.AI/LlmNeuron.cs` and `SignalText.cs` via `git stash`

- [ ] **Step 1: Stash the foreign uncommitted edits**

```bash
cd D:/digitalbrain
git stash push -m "pre-revert: uncommitted LlmNeuron/SignalText edits" -- src/Modules/AI
git status --short   # must be clean
```

- [ ] **Step 2: Revert the commit**

```bash
git revert --no-commit 7e55ef26
git status --short
```
Expected: `Signal.cs` regains `MaxBodyBytes`; `Neuron.cs` loses `ScheduleTurn`; docs 01-04 and `grok-benchmark.md` return to their prior text; `src/Modules/AI/**`, `tests/.../AiSteps.cs`, `ScriptedChatClient.cs`, `groupchat.feature`, `llm.feature` (if present) are deleted; `AppHost.cs`, AppHost csproj, Silo csproj, tests csproj, `DigitalBrain.slnx`, `Directory.Packages.props` lose the AI lines; `BrainTools.cs` description regains "up to 64 KB".

- [ ] **Step 3: Re-apply the one doc-01 sentence that must change**

In `docs/architecture/01-communication-model.md`, replace the sentence
`A reply is fired from a later turn: a timer, a reminder, or the next incoming signal. Making \`Deliver\` one-way and queued, so a reply can be immediate, is the next phase.`
with
`\`Deliver\` accepts; the receiving neuron reacts in its own later turn from its inbox (see doc 05). A reply fired from inside \`ReceiveAsync\` is therefore ordinary.`

- [ ] **Step 4: Build, test, commit**

```bash
dotnet build DigitalBrain.slnx
dotnet test tests/DigitalBrain.Tests
git add -A
git commit -m "revert: ScheduleTurn, cap removal and in-grain groupchat (7e55ef26); doc 05 supersedes"
```
Expected: 41 tests green (the pre-commit baseline).

---

### Task 2: Inbox drain in the kernel

**Files:**
- Create: `src/Kernel/DigitalBrain.Contracts/Neurons/INeuronDrain.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/Neuron.cs`, `NeuronRuntime.cs`, `NeuronJournals.cs`, `JournalWindow.cs`
- Delete: `src/Kernel/DigitalBrain/Neuron/NeuronRequestPath.cs`
- Test: `tests/DigitalBrain.Tests/Features/react.feature`, `Features/ReactSteps.cs`, `Features/Fixtures.cs` (add fixtures)

**Interfaces produced:**
```csharp
namespace DigitalBrain.Abstractions.Neurons;
[Alias("db.v3.neuron-drain")]
public interface INeuronDrain : IGrainWithStringKey { [OneWay] [Alias(nameof(Drain))] Task Drain(); }

// Neuron (runtime)
protected virtual Task ReceiveAsync(SignalDelivery delivery, CancellationToken ct); // now runs in the neuron's own turn
protected SignalDelivery? CurrentDelivery { get; }                                  // set during ReceiveAsync
```

- [ ] **Step 1: Write the failing feature**

`react.feature`:
```gherkin
Feature: React
  Deliver accepts. A neuron reacts to its inbox in its own turns, in order, exactly once,
  and may fire back at the neuron that delivered to it.

  Scenario: An echo neuron replies to its source from inside its reaction
    Given a running brain
    When "claude" fires "Ping" {"n":1} at echo "e"
    And "claude" waits up to 5 seconds for an incoming "Pong"
    Then "claude" incoming journal contains "Pong" {"n":1}
    And the latest "claude" incoming entry has the same correlation as the latest "claude" outgoing entry

  Scenario: Reactions run in journal order
    Given a running brain
    When "claude" fires "Ping" {"n":1} at echo "e"
    And "claude" fires "Ping" {"n":2} at echo "e"
    And "claude" fires "Ping" {"n":3} at echo "e"
    And "claude" waits up to 5 seconds for 3 incoming "Pong"
    Then "claude" incoming "Pong" bodies are {"n":1}, {"n":2}, {"n":3}

  Scenario: A reaction that throws is retried and the entry is not lost
    Given a running brain
    And flaky "f" fails its first reaction
    When "claude" fires "Ping" {"n":1} at flaky "f"
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then flaky "f" reacted 2 times to sequence 1

  Scenario: Unreacted entries survive a restart
    Given a running brain with durable storage
    And sleepy "s" is asleep
    When "claude" fires "Ping" {"n":1} at sleepy "s"
    And the silo restarts
    And sleepy "s" is awake
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then "claude" incoming journal contains "Pong" {"n":1}

  Scenario: Fire returns once the target accepted, before it reacted
    Given a running brain
    And sleepy "s" is asleep
    When "claude" fires "Ping" {"n":1} at sleepy "s"
    Then the fire reached 1 neurons
    And sleepy "s" incoming journal contains "Ping" {"n":1}
    And "claude" incoming journal is empty
```

- [ ] **Step 2: Fixtures** (append to `Fixtures.cs`)

```csharp
// Replies Pong{n} to the source of every Ping{n}, from inside ReceiveAsync.
[GrainType("echo")]
internal sealed class EchoNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken ct)
        => delivery.Signal.Type == "Ping"
            ? FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, ct)
            : Task.CompletedTask;
}

// Test switches shared across activations of fixture grains in one silo.
public static class FixtureSwitches
{
    public static readonly ConcurrentDictionary<string, int> FlakyFailuresLeft = new();
    public static readonly ConcurrentDictionary<string, int> Reactions = new();
    public static readonly ConcurrentDictionary<string, bool> Asleep = new();
}

// Throws on the first reaction to each entry while FlakyFailuresLeft[name] > 0, then echoes.
[GrainType("flaky")]
internal sealed class FlakyNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken ct)
    {
        var key = $"{Id.Name}:{delivery.Sequence}";
        FixtureSwitches.Reactions.AddOrUpdate(key, 1, (_, n) => n + 1);
        if (FixtureSwitches.FlakyFailuresLeft.AddOrUpdate(Id.Name, 0, (_, left) => left - 1) >= 0)
        {
            throw new InvalidOperationException("flaky: first reaction fails");
        }
        return FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, ct);
    }
}

// While Asleep[name] is true the reaction throws (so the cursor stays); when awake it echoes.
[GrainType("sleepy")]
internal sealed class SleepyNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken ct)
    {
        if (FixtureSwitches.Asleep.TryGetValue(Id.Name, out var asleep) && asleep)
        {
            throw new InvalidOperationException("sleepy: not yet");
        }
        return FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, ct);
    }
}
```
`FlakyFailuresLeft` semantics: the step sets it to 1; `AddOrUpdate` returns the *new* value, so the first reaction sees 0 (≥ 0 → throw) and the second sees -1 (proceed). `Reactions[key]` therefore ends at 2.

- [ ] **Step 3: Steps** (`ReactSteps.cs`)

```csharp
[Binding]
public sealed class ReactSteps(BrainSteps brain)
{
    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at (echo|flaky|sleepy) ""(.*)""")]
    public async Task FireAtTyped(string from, string type, string body, string grainType, string name)
        => await brain.FireCore(from, type, body, new NeuronId(grainType, name));

    [Given(@"flaky ""(.*)"" fails its first reaction")]
    public void GivenFlaky(string name) => FixtureSwitches.FlakyFailuresLeft[name] = 1;

    [Given(@"sleepy ""(.*)"" is asleep")]
    public void GivenAsleep(string name) => FixtureSwitches.Asleep[name] = true;

    [When(@"sleepy ""(.*)"" is awake")]
    public async Task WhenAwake(string name)
    {
        FixtureSwitches.Asleep[name] = false;
        // Touching the neuron activates it; activation resumes the drain.
        _ = await brain.Brain.Grains.GetGrain<INeuronQuery>(new NeuronId("sleepy", name).ToGrainId()).ReadState();
    }

    [When(@"""(.*)"" waits up to (\d+) seconds for an incoming ""(\w+)""")]
    public Task WaitOne(string neuron, int seconds, string type) => WaitMany(neuron, seconds, 1, type);

    [When(@"""(.*)"" waits up to (\d+) seconds for (\d+) incoming ""(\w+)""")]
    public async Task WaitMany(string neuron, int seconds, int count, string type)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var read = await brain.Journal(neuron, JournalKind.Incoming);
            if (read.Delta.Count(d => d.Signal.Type == type) >= count) return;
            await Task.Delay(50);
        }
        Assert.Fail($"{neuron} did not receive {count} {type} within {seconds}s");
    }

    [Then(@"""(.*)"" incoming ""(\w+)"" bodies are (.*)$")]
    public async Task ThenBodies(string neuron, string type, string expected)
    {
        var bodies = (await brain.Journal(neuron, JournalKind.Incoming)).Delta.Where(d => d.Signal.Type == type).Select(d => d.Signal.Body);
        Assert.Equal(expected, string.Join(", ", bodies));
    }

    [Then(@"the latest ""(.*)"" incoming entry has the same correlation as the latest ""(.*)"" outgoing entry")]
    public async Task ThenSameCorrelation(string a, string b)
        => Assert.Equal((await brain.Journal(b, JournalKind.Outgoing)).Delta[^1].CorrelationId, (await brain.Journal(a, JournalKind.Incoming)).Delta[^1].CorrelationId);

    [Then(@"flaky ""(.*)"" reacted (\d+) times to sequence (\d+)")]
    public void ThenReacted(string name, int times, long seq) => Assert.Equal(times, FixtureSwitches.Reactions[$"{name}:{seq}"]);

    [Then(@"(?:echo|flaky|sleepy) ""(.*)"" incoming journal contains ""(\w+)"" (\{.*\})$")]
    public async Task ThenTypedIncoming(string name, string type, string body)
    {
        // grain type is inferred from the scenario; try all three fixture types
        foreach (var t in new[] { "echo", "flaky", "sleepy" })
        {
            var read = await brain.Brain.Grains.GetGrain<INeuronQuery>(new NeuronId(t, name).ToGrainId()).ReadJournal(JournalKind.Incoming, 0);
            if (read.Delta.Any(d => d.Signal.Type == type && d.Signal.Body == body)) return;
        }
        Assert.Fail($"no {type} {body} on {name}");
    }
}
```
`BrainSteps.FireCore` needs an overload taking a `NeuronId? to` (add it; keep the string one delegating to it).

- [ ] **Step 4: Run, see red** — `dotnet test tests/DigitalBrain.Tests --filter "FullyQualifiedName~React"`. The echo scenario fails today with the cycle-guard exception surfacing from `Fire`.

- [ ] **Step 5: Kernel implementation**

`INeuronDrain.cs` as in Interfaces. `Neuron` implements it.

`NeuronRuntime.Bind`: add `services.GetRequiredKeyedService<IDurableValue<long>>("reacted")` to `NeuronActivationComponents` as `Reacted`.

`JournalWindow`: add
```csharp
internal long LastSequence => _lastSequence.Value;
internal bool TryRead(long sequence, out SignalDelivery delivery)
{
    delivery = null!;
    var earliest = EarliestRetainedSequence();
    if (sequence < earliest || sequence > _lastSequence.Value) return false;
    var encoded = _retained[(int)(sequence - earliest)];
    using var session = _sessions.GetSession();
    var reader = Reader.Create(encoded, session);
    delivery = _entries.Deserialize(ref reader).Delivery;
    return true;
}
```
`NeuronJournals`: `internal long IncomingLastSequence => incoming.LastSequence;` and `internal bool TryReadIncoming(long seq, out SignalDelivery d) => incoming.TryRead(seq, out d);`

`Neuron.cs`:
```csharp
public async Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(delivery);
    cancellationToken.ThrowIfCancellationRequested();
    if (_components.Latest.Count >= MaxSignalTypesPerNeuron && !_components.Latest.ContainsKey(delivery.Signal.Type))
    {
        throw new SignalRejectedException(/* unchanged text */);
    }
    _components.Journals.AppendIncoming(delivery);
    _components.Latest[delivery.Signal.Type] = delivery;
    await WriteStateAsync(cancellationToken).ConfigureAwait(true);
    Wake();
}

// One-way self message: queued behind the current turn, never awaited.
private void Wake() => GrainFactory.GetGrain<INeuronDrain>(this.GetGrainId()).Drain().Ignore();

protected override async Task OnNeuronActivatedAsync(...)  // keep the virtual; call Wake() from OnActivateAsync after base activation when Reacted < IncomingLastSequence

// Reacts to exactly one pending entry per call, then re-wakes if more remain.
public async Task Drain()
{
    var next = _components.Reacted.Value + 1;
    if (next > _components.Journals.IncomingLastSequence) return;

    if (!_components.Journals.TryReadIncoming(next, out var delivery))
    {
        // Fell out of the retained window before we reacted: count it as lost and move on.
        DrainTelemetry.Lost(Id, next);
        _components.Reacted.Value = next;
        await WriteStateAsync().ConfigureAwait(true);
        Wake();
        return;
    }

    var previous = _handling;
    _handling = delivery;
    try
    {
        await ReceiveAsync(delivery, CancellationToken.None).ConfigureAwait(true);
    }
    catch (Exception failure)
    {
        // Cursor stays; the next Deliver or activation retries. Do not spin.
        DrainTelemetry.Failed(Id, next, failure);
        return;
    }
    finally
    {
        _handling = previous;
    }

    _components.Reacted.Value = next;
    await WriteStateAsync().ConfigureAwait(true);
    Wake();
}
```
`DrainTelemetry`: a tiny static with an `ActivitySource("DigitalBrain")` emitting `db.drain.lost` / `db.drain.failed` activities with tags neuron, sequence, exception type; and an `ILogger` warning if a logger is available via `ServiceProvider`. Keep it under 40 lines in `Neuron/DrainTelemetry.cs`.

Retry semantics for the flaky scenario: after a failure nothing wakes the neuron until the next `Deliver` or activation. To make the flaky scenario pass without a timer, re-wake once after failure with a bounded backoff: `this.RegisterGrainTimer(_ => { Wake(); return Task.CompletedTask; }, new GrainTimerCreationOptions { DueTime = TimeSpan.FromMilliseconds(250), Period = Timeout.InfiniteTimeSpan, Interleave = false })`. The timer is a *wake-up*, not the reaction, so durability still lives in the cursor; if the timer is lost, the next activation resumes. Cap consecutive failures per activation at 5 before waiting for an external wake (a `private int _consecutiveFailures`), so a permanently failing reaction does not hot-loop.

Remove `NeuronRequestPath` and its `using` in `FireAsync`. `FireAsync` otherwise unchanged. Delete `NeuronRequestPath.cs`.

`NeuronConcurrency`: `Drain` is a normal (non-interleaved) grain method; nothing to change. `INeuronDrain` must be excluded from the "no ReadOnly outside INeuronQuery" check only if it carries such attributes; it does not.

- [ ] **Step 6: Run the whole suite** — expected: all previous scenarios still green (fire/connect/read semantics unchanged at accept), five React scenarios green. If "Reactions run in journal order" flakes, the drain is reacting to more than one entry per call or `Wake` is not queued; fix, do not loosen the test.

- [ ] **Step 7: Commit** — `feat: inbox drain — Deliver accepts, neurons react in their own turns`

---

### Task 3: AI module scaffolding in the three-project shape

**Files:**
- Create: `src/Modules/AI/Contracts/DigitalBrain.Modules.AI.Contracts.csproj`, `AIVocabulary.cs`
- Create: `src/Modules/AI/AI/DigitalBrain.Modules.AI.csproj`, `AIModule.cs`, `Providers.cs`, `Bodies.cs`
- Create: `src/Modules/AI/Aspire.Hosting/DigitalBrain.Modules.AI.Aspire.Hosting.csproj`, `AIHostingExtensions.cs`
- Modify: `DigitalBrain.slnx` (three projects under `/Modules/AI/`), `src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj` (reference module), `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` (reference hosting), `src/Aspire/DigitalBrain.AppHost/AppHost.cs` (one line `.AddAI()`), `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` (reference module), `Directory.Packages.props` (no new packages needed: Agents.AI, Agents.AI.Workflows, Extensions.AI, Extensions.AI.OpenAI already pinned)
- Create: `tests/DigitalBrain.Tests/Features/ScriptedChatClient.cs`, `Features/AiSteps.cs` (Given only for now)

**Interfaces produced:**
```csharp
namespace DigitalBrain.AI.Contracts;
public static class AIVocabulary {
    public const string AgentType = "agent"; public const string ChatType = "chat";
    public const string Instruct = "Instruct", Ask = "Ask", Reply = "Reply", Turn = "Turn", Said = "Said";
}
namespace DigitalBrain.AI;
public sealed class AIModule : IModule;                       // keyed IChatClient per provider + "default"
internal static class Providers { static IChatClient Resolve(IServiceProvider sp, string? provider); }
internal static class Bodies { Text(body) ; Write(text) ; Said(author,text) ; Instruct(body) -> InstructBody record }
// Aspire
public static DigitalBrainBuilder AddAI(this DigitalBrainBuilder brain, Action<AIHostingOptions>? configure = null);
```

- [ ] **Step 1: Contracts project**

csproj: `IsPackable true`, description "AI module vocabulary: grain type names and documented signal body shapes.", `ProjectReference ../../../Kernel/DigitalBrain.Contracts`. `AIVocabulary.cs` with the constants above plus XML doc comments describing each body:
- `Instruct` (agent): `{ "provider": "xai|openai|scripted", "model": "grok-4.6", "system": "...", "tools": ["websearch"] }`
- `Instruct` (chat): `{ "participants": ["agent:writer","agent:reviewer"], "manager": "roundrobin", "rounds": 2 }`
- `Ask`: `{ "text": "..." }`; `Reply`: `{ "text": "..." }`; `Turn`: `{}`; `Said`: `{ "author": "agent:writer", "text": "..." }`

- [ ] **Step 2: Module project + AIModule**

csproj: RootNamespace `DigitalBrain.AI`, packages `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, `Microsoft.Orleans.Sdk`; references `../Contracts/...`, `../../../Kernel/DigitalBrain/DigitalBrain.csproj`, `../../../Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj` (for `BrainOperations` read). `NoWarn CA1812`.

`AIModule.Configure(ISiloBuilder)`:
```csharp
var cfg = builder.Configuration.GetSection("DigitalBrain:AI");
foreach (var p in cfg.GetSection("Providers").GetChildren())   // DigitalBrain:AI:Providers:xai:{Endpoint,ApiKey,Model}
{
    var name = p.Key; var endpoint = p["Endpoint"] ?? "https://api.x.ai/v1"; var key = p["ApiKey"]; var model = p["Model"] ?? "grok-4.6";
    if (string.IsNullOrWhiteSpace(key)) continue;
    builder.Services.AddKeyedSingleton<IChatClient>(name, (_, _) =>
        new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(key), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(model).AsIChatClient());
}
builder.Services.TryAddSingleton(new AIDefaults(cfg["DefaultProvider"] ?? "xai"));
builder.Services.TryAddSingleton<NativeTools>();   // empty registry: Dictionary<string, AIFunction>; modules add later
```
Environment fallback: if no providers configured and `XAI_API_KEY` is set, register `xai` with it. `Providers.Resolve(sp, provider)` returns the keyed client or throws `SignalRejectedException($"Provider '{provider}' is not configured. Configure DigitalBrain:AI:Providers:{provider} or omit provider to use '{default}'.")`.

- [ ] **Step 3: Aspire hosting project**

csproj references `../AI/DigitalBrain.Modules.AI.csproj` and `../../../Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`. `AIHostingExtensions.AddAI(this DigitalBrainBuilder brain, Action<AIHostingOptions>? configure = null)`: calls `brain.AddModule<AIModule>(m => m.AddProjection(new AIProjection(options)))`; `AIProjection.Apply` sets `DigitalBrain__AI__DefaultProvider` and, for each provider in options, `DigitalBrain__AI__Providers__{name}__Endpoint/Model` and the api key from an Aspire parameter (`builder.AddParameter($"ai-{name}-apikey", secret: true)`) via `WithEnvironment`. `AIHostingOptions` has `AddProvider(name, endpoint, model)` and `DefaultProvider`. Default: one provider `xai` at `https://api.x.ai/v1`, model `grok-4.6`, key parameter `ai-xai-apikey`.

`AppHost.cs`: `var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain).AddAI();` and `using DigitalBrain.AI.Aspire.Hosting;`.

- [ ] **Step 4: Test scaffolding**

`ScriptedChatClient : IChatClient` in tests: a queue of scripted responses; each item is either text or a function call `(name, args)`. `GetResponseAsync` dequeues: for a function call it returns a message with `FunctionCallContent`; the next call (which will contain the `FunctionResultContent`) dequeues the next item. `GetStreamingResponseAsync` wraps the non-streaming. Record all received messages in `Calls` for assertions.

`AiSteps.Given("a running brain with AI")`: `BrainSimulation.StartAsync(new() { Modules = new([typeof(AIModule)]), ConfigureSilo = silo => { silo.Services.AddKeyedSingleton<IChatClient>("scripted", (_, _) => world.Scripted); silo.Services.AddSingleton(new AIDefaults("scripted")); } })` where `BrainWorld` gains `public ScriptedChatClient Scripted { get; } = new();`. Also `Given the scripted model will say "..."` and `Given the scripted model will call tool "read" with {...} then say "..."`.

- [ ] **Step 5: Build, test (still 46 green), commit** — `feat: AI module scaffolding — Contracts, module, Aspire hosting`

---

### Task 4: The `agent` neuron

**Files:**
- Create: `src/Modules/AI/AI/AgentNeuron.cs`, `AgentState.cs`, `BrainTools.cs`
- Test: `tests/DigitalBrain.Tests/Features/agent.feature`, extend `AiSteps.cs`

- [ ] **Step 1: Failing feature**

```gherkin
Feature: agent
  An agent is a Session neuron with a model attached. It runs a MAF agent over the
  provider named by Instruct, with the four brain operations as tools.

  Scenario: Ask is answered with Reply on the same correlation
    Given a running brain with AI
    And the scripted model will say "pong"
    When session "claude" fires "Ask" {"text":"ping"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then reading "claude" shows latest "Reply" {"text":"pong"}
    And the latest "claude" incoming entry has the same correlation as the latest "claude" outgoing entry

  Scenario: Instruct is the system prompt and provider
    Given a running brain with AI
    And the scripted model will say "ok"
    When session "claude" fires "Instruct" {"provider":"scripted","system":"You are terse."} at "agent:a"
    And session "claude" fires "Ask" {"text":"hi"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model received a system message "You are terse."

  Scenario: The agent uses a brain tool and it shows in its journals
    Given a running brain with AI
    And session "claude" fires "Note" {"text":"deploy on fridays"} at "policy"
    And the scripted model will call tool "read" with {"neuron":"policy","what":"state"} then say "fridays"
    When session "claude" fires "Ask" {"text":"when do we deploy?"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model received a tool result containing "deploy on fridays"
    And reading "claude" shows latest "Reply" {"text":"fridays"}

  Scenario: The agent fires through a tool and the fire is journaled on the agent
    Given a running brain with AI
    And the scripted model will call tool "fire" with {"type":"Note","body":"{\"text\":\"remembered\"}","to":"memo"} then say "done"
    When session "claude" fires "Ask" {"text":"remember this"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then "agent:a" outgoing journal contains "Note" {"text":"remembered"}
    And "memo" incoming journal contains "Note" {"text":"remembered"}

  Scenario: Two conversations do not share a session
    Given a running brain with AI
    And the scripted model will say "one"
    And the scripted model will say "two"
    When session "claude" fires "Ask" {"text":"a"} at "agent:a"
    And session "bob" fires "Ask" {"text":"b"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    And "bob" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model saw 2 conversations with 1 user message each

  Scenario: An unknown provider is a Reply that says what to configure
    Given a running brain with AI
    When session "claude" fires "Instruct" {"provider":"nope"} at "agent:a"
    And session "claude" fires "Ask" {"text":"hi"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the latest "claude" incoming "Reply" text contains "Provider 'nope' is not configured"
```
Steps: `"X" outgoing journal contains "T" {...}` and `"X" incoming journal contains` already exist in `BrainSteps` for plain names; add typed-name variants (`agent:a`) by making `BrainSteps.Id` use `NeuronId.TryParse`. The `waits` steps come from `ReactSteps`.

- [ ] **Step 2: Implementation**

`AgentState`:
```csharp
[GenerateSerializer] [Alias("db.ai.agent-state")]
public sealed record AgentState([property: Id(0)] List<AgentSessionEntry> Sessions)   // newest last, max 32
{ public static AgentState Empty => new([]); }
[GenerateSerializer] [Alias("db.ai.agent-session")]
public sealed record AgentSessionEntry([property: Id(0)] string Correlation, [property: Id(1)] string SessionJson);
```

`BrainTools.For(AgentNeuron neuron, BrainOperations reads)` returns four `AIFunction`s via `AIFunctionFactory.Create`:
- `fire(type, body, to?, correlation?)` → `neuron.FireFromTool(...)` which calls `FireAsync` in-process (never a grain call to self) and returns the `FireOutcome` as JSON.
- `connect(from, to, type)` / `disconnect` → if `from` is the agent itself call `Connect`/`Disconnect` in-process, else `grains.GetGrain<INeuron>(from).Connect(...)`.
- `read(neuron, what?, after, timeoutSeconds)` → `reads.ReadAsync(...)` with `timeoutSeconds` forced to 0 (never wait inside a turn); JSON string result.
Descriptions copied from the MCP `BrainTools` so the model sees identical tools.

`AgentNeuron : Neuron<AgentState>`, `[GrainType(AIVocabulary.AgentType)]`, ctor `(NeuronRuntime, [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AgentState>, IServiceProvider services)`:
```csharp
protected override async Task ReceiveAsync(SignalDelivery d, CancellationToken ct)
{
    if (d.Signal.Type is not (AIVocabulary.Ask or AIVocabulary.Turn)) return;
    var instruct = Bodies.Instruct(LatestBody(AIVocabulary.Instruct));      // provider, model, system, tools
    IChatClient client;
    try { client = Providers.Resolve(services, instruct.Provider); }
    catch (SignalRejectedException e) { await ReplyAsync(d, e.Message, ct); return; }

    var tools = BrainTools.For(this, new BrainOperations(GrainFactory)).Concat(NativeTools(instruct.Tools)).ToList();
    var agent = new ChatClientAgent(client, new ChatClientAgentOptions { Name = Id.Name, Instructions = instruct.System, ChatOptions = new() { Tools = tools } });

    var session = await LoadSessionAsync(agent, d.CorrelationId).ConfigureAwait(true);
    var input = d.Signal.Type == AIVocabulary.Turn ? await TurnContextAsync(d).ConfigureAwait(true) : Bodies.Text(d.Signal.Body);
    var response = await agent.RunAsync(input, session, cancellationToken: ct).ConfigureAwait(true);
    await SaveSessionAsync(agent, d.CorrelationId, session).ConfigureAwait(true);

    var type = d.Signal.Type == AIVocabulary.Turn ? AIVocabulary.Said : AIVocabulary.Reply;
    var body = type == AIVocabulary.Said ? Bodies.Said(Id.ToString(), response.Text) : Bodies.Write(response.Text);
    await FireAsync(Signal.Create(type, body), d.Source, d.CorrelationId, ct).ConfigureAwait(true);
}
```
`LatestBody(type)` reads `_components`-free: use `ReadState()` (own query, in-process call is fine) and pick the type. `LoadSessionAsync`: find entry by correlation → `agent.DeserializeSessionAsync(JsonDocument.Parse(json).RootElement)` else `agent.CreateSessionAsync()`. `SaveSessionAsync`: serialize, upsert, trim to 32, `SaveAsync(state)`. `TurnContextAsync(d)`: read `d.Source`'s incoming journal (via `INeuronQuery`) for entries with `d.CorrelationId`, build a single user message: the `Ask` text followed by each `Said` as `"{author}: {text}"` lines; the model then speaks as the next participant. `ReplyAsync(d, text, ct)` fires `Reply {text}` at `d.Source`.

Exact MAF type names (`ChatClientAgent`, `ChatClientAgentOptions`, `AgentSession`, `RunAsync(string, AgentSession, AgentRunOptions?, ct)`, `SerializeSessionAsync`/`DeserializeSessionAsync`) must be checked against the installed 1.19.0 package XML docs under `~/.nuget/packages/microsoft.agents.ai/1.19.0/`; if `AgentThread` is the name instead of `AgentSession` in this version, use it and note it in the report.

- [ ] **Step 3: Run agent.feature to green; whole suite green; commit** — `feat: agent neuron — MAF ChatClientAgent with the four brain operations as tools`

---

### Task 5: Spike, then the `chat` neuron

**Files:**
- Spike (throwaway, not committed): `tests/DigitalBrain.Tests/Features/ChatSpikeFacts.cs`
- Create: `src/Modules/AI/AI/ChatNeuron.cs`, `ChatState.cs`
- Test: `tests/DigitalBrain.Tests/Features/chat.feature`, extend `AiSteps.cs`

- [ ] **Step 1: Spike (time-box 60 minutes): how does a group-chat participant halt the workflow for an external answer?**

Write one xunit fact that builds a MAF group-chat workflow with two participants and a `RoundRobinGroupChatManager { MaximumIterationCount = 2 }`, runs it with `CheckpointManager.CreateJson(new InMemoryJsonCheckpointStore())` (or the file-system store into a temp dir), and tries, in this order, stopping at the first that works:
  (a) participants added as executors wired to a `RequestPort` (if `GroupChatWorkflowBuilder.AddParticipants` accepts executors or the builder exposes the underlying `WorkflowBuilder`);
  (b) participants as a proxy `AIAgent` subclass whose `RunCoreAsync` returns a response containing a request content that the workflow surfaces as `RequestInfoEvent` (the pattern the "GroupChatToolApproval" sample uses with `FunctionApprovalRequestContent`), then resume with `SendResponseAsync`/`ResumeStreamingAsync` from the captured checkpoint;
  (c) no workflow runtime: instantiate `RoundRobinGroupChatManager` (or the abstract `GroupChatManager`) directly and call its speaker-selection/termination API per turn, persisting only our own turn index.
Record in the report which option worked, the exact API calls, and whether the checkpoint survives being serialized to a string and back (needed for the snapshot). The `chat` neuron uses the first option that worked. If only (c) works, the doc-05 sentence "checkpoints into its snapshot" becomes "persists the manager's turn state", and the report says so.

- [ ] **Step 2: Failing feature**

```gherkin
Feature: chat
  A chat is participants (agent neurons), a transcript (the chat's incoming journal) and a
  turn policy (MAF group chat). Every Said is a real signal on a real synapse.

  Scenario: Instruct wires the anatomy
    Given a running brain with AI
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    Then "chat:design" has a synapse to "agent:writer" for "Turn"
    And "chat:design" has a synapse to "agent:reviewer" for "Turn"
    And "agent:writer" has a synapse to "chat:design" for "Said"
    And "agent:reviewer" has a synapse to "chat:design" for "Said"

  Scenario: Two participants take turns and the asker gets one Reply
    Given a running brain with AI
    And the scripted model will say "draft: brain"
    And the scripted model will say "review: ship it"
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    And session "claude" fires "Ask" {"text":"a slogan"} at "chat:design"
    And "claude" waits up to 20 seconds for an incoming "Reply"
    Then "chat:design" incoming "Said" bodies are {"author":"agent:writer","text":"draft: brain"}, {"author":"agent:reviewer","text":"review: ship it"}
    And "claude" incoming journal has 1 entries
    And the latest "claude" incoming "Reply" text contains "ship it"

  Scenario: The transcript is readable with no participant alive
    Given a running brain with durable storage and AI
    And the scripted model will say "one"
    And the scripted model will say "two"
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    And session "claude" fires "Ask" {"text":"go"} at "chat:design"
    And "claude" waits up to 20 seconds for an incoming "Reply"
    And the silo restarts
    Then "chat:design" incoming journal has 3 entries

  Scenario: A chat resumes after a restart mid-conversation
    Given a running brain with durable storage and AI
    And the scripted model will say "one"
    And the scripted model will pause before its next answer
    And the scripted model will say "two"
    When session "claude" fires "Instruct" {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":1} at "chat:design"
    And session "claude" fires "Ask" {"text":"go"} at "chat:design"
    And "chat:design" waits up to 10 seconds for an incoming "Said"
    And the silo restarts
    And the scripted model is unpaused
    And "claude" waits up to 30 seconds for an incoming "Reply"
    Then "chat:design" incoming "Said" bodies are {"author":"agent:writer","text":"one"}, {"author":"agent:reviewer","text":"two"}

  Scenario: Ask before Instruct explains what to do
    Given a running brain with AI
    When session "claude" fires "Ask" {"text":"hi"} at "chat:empty"
    And "claude" waits up to 5 seconds for an incoming "Reply"
    Then the latest "claude" incoming "Reply" text contains "Instruct this chat with at least two participants"
```
"pause" in `ScriptedChatClient`: a `TaskCompletionSource` the client awaits before answering when a pause item is dequeued; the restart scenario relies on the *agent's* reaction failing (cancelled by silo shutdown) and being retried after restart via the drain. The paused participant's `Turn` entry is unreacted at shutdown, so after restart the drain retries and the scripted client (now unpaused, and re-registered by `ConfigureSilo`) answers.

- [ ] **Step 3: Implementation**

`ChatState`: `[GenerateSerializer]` record with `List<ChatRun>`; `ChatRun(string Correlation, string Asker /*NeuronId text*/, string CheckpointJson /*or turn index if option (c)*/, string? PendingParticipant)`. Bounded to 16 runs.

`ChatNeuron : Neuron<ChatState>`, `[GrainType(AIVocabulary.ChatType)]`:
- On `Instruct`: parse participants; for each `p`: `Connect(p, Turn)` (own synapse) and `GrainFactory.GetGrain<INeuron>(p).Connect(Id, Said)` (their synapse to us). Disconnect participants no longer listed.
- On `Ask`: if fewer than 2 participants → `Reply` with the advice text. Else start the run per the spike's option: build the workflow (participants as proxies named by neuron id), run until it halts asking participant `p`; persist `ChatRun` (checkpoint + pending) via `SaveAsync`; `FireAsync(Turn {}, p, correlation)`.
- On `Said` (must carry a known correlation and come from the pending participant; otherwise journal-only): resume from the checkpoint with the `Said` text as the response; if it halts again → persist and fire next `Turn`; if it completes → `FireAsync(Reply {text = last Said text}, asker, correlation)` and remove the run.
- Anything else: ignored.
A `Turn` fired from inside `ReceiveAsync` at a participant, whose `Said` comes back as our next inbox entry, is exactly the drain contract.

- [ ] **Step 4: Green, whole suite green, delete the spike file, commit** — `feat: chat neuron — MAF group chat over agent neurons; transcript is the journal`

---

### Task 6: Live run through the brain

- [ ] **Step 1:** `aspire start` the AppHost; confirm `/health` 200 and `brain` tools list. Provide the xAI key via the Aspire parameter (`ai-xai-apikey`) or `XAI_API_KEY`.
- [ ] **Step 2:** From Claude Code with the `brain` server: `fire Instruct {"provider":"xai","system":"You are a pessimistic reviewer."} at agent:reviewer`; `fire Instruct {"provider":"xai","system":"You are an upbeat copywriter."} at agent:writer`; `fire Instruct {"participants":["agent:writer","agent:reviewer"],"manager":"roundrobin","rounds":2} at chat:slogan`; `fire Ask {"text":"One-line slogan for a neuron/synapse framework"} at chat:slogan`; `read claude what=incoming timeoutSeconds=60` until `Reply`; `read chat:slogan what=incoming` for the transcript.
- [ ] **Step 3:** Repeat step 2 through Grok CLI as principal `grok` with the same prompts phrased in English; score 0-2 per step as in `grok-benchmark.md`; append a "Round 2: agents and chat" section there.
- [ ] **Step 4:** Ask Claude, via the `brain` tools only, to add a third participant mid-life (`fire Instruct` with three participants) and confirm the anatomy changed with `read chat:slogan what=synapses`.
- [ ] **Step 5:** Commit docs — `docs: live agent/chat run through brain`.

---

## Self-review

- Spec coverage: drain invariants (Task 2 scenarios 1-5); agent invariants 1, 3, 6 (Task 4); chat invariants 1, 2, 4, 5, 7 (Task 5; 7 via the restart scenario's retry); module shape and AppHost rule (Task 3); vocabulary (Contracts). ✔
- Placeholders: the spike is explicitly bounded with an ordered fallback and a documented consequence; MAF type names are flagged for verification against the installed package. No TBDs.
- Type consistency: `AIVocabulary` constants, `Bodies` helpers, `FireCore(string, string, string, NeuronId?)`, `waits up to N seconds for [N] incoming "T"` steps, `ScriptedChatClient` API (`Say`, `CallTool`, `Pause`, `Unpause`, `Calls`) are used identically across Tasks 2-5.
