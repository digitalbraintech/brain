# Core Communication Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut DigitalBrain down to the approved two-noun/two-verb model (Neuron, Synapse; Fire, Connect) with Read as a query, latest-signal-per-type state, a 64 KB membrane, and a four-tool MCP surface, covered by Reqnroll features.

**Architecture:** `DigitalBrain.Contracts` holds the grain interfaces and records. `DigitalBrain` is the Orleans runtime (`Neuron : DurableGrain`, `Neuron<TState>` adding a snapshot facet, `PlainNeuron` as the default type). `DigitalBrain.Mcp` is the only client: four plain C# operations over records, wrapped as MCP tools. `DigitalBrain.Testing` keeps the in-process cluster harness. `tests/DigitalBrain.Tests` drives everything through the four operations.

**Tech Stack:** .NET 11 preview, Orleans 10.2.2 (+ Orleans.Journaling 10.2.2-rc.2.alpha.1), ModelContextProtocol 2.2.0, Reqnroll 3.3.4 on xunit v3, Aspire 13.5 (untouched except three forced edits listed in Task 1).

**Spec:** `docs/architecture/01-communication-model.md`, `02-working-style-memory.md`, `03-mcp-surface.md`, `04-testing-and-projects.md`.

## Global Constraints

- One brain per silo. `NeuronId` is `(Type, Name)`; no owner or principal anywhere.
- Signal = `(Type, Body)` where `Type` is letters only (`^[A-Za-z]{1,64}$`) and `Body` is a JSON text of at most 65_536 UTF-8 bytes.
- Journals: 512 entries / 512 KB per window; tallies and sequences survive compaction and restart (existing `JournalWindow` behaviour, keep it).
- Read never writes: `INeuronQuery` methods stay `[ReadOnly] [AlwaysInterleave]`.
- The Aspire folder is not redesigned. Only the three edits in Task 1 step 9 are allowed there, and they exist solely because `DigitalBrain.Sdk` is deleted.
- Namespaces stay as they are (`DigitalBrain.Abstractions` in Contracts, `DigitalBrain.Core` in the runtime) to avoid churn in Aspire.
- `TreatWarningsAsErrors` is on with `AnalysisLevel=preview-all`. Every task ends with `dotnet build DigitalBrain.slnx` clean and `dotnet test tests/DigitalBrain.Tests` green.
- Commit after every task with the trailer:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BsRRKgTHxZbtHL94dVL9e1
  ```

---

## File map

**Contracts (`src/Kernel/DigitalBrain.Contracts`)** — after Task 1 it contains exactly:

| File | Responsibility |
|---|---|
| `DigitalBrainNames.cs` | unchanged constants (Aspire reads them) |
| `Identity/NeuronId.cs` | `(Type, Name)`, grain id mapping |
| `Identity/IdentityPart.cs` | name validation |
| `Identity/SignalId.cs`, `Identity/CorrelationId.cs` | unchanged |
| `Signals/Signal.cs` | `Signal(Type, Body)` + validation + `SignalRejectedException` |
| `Signals/SignalDelivery.cs` | the envelope |
| `Synapses/Synapse.cs` | `(Source, Target, SignalType, CreatedAt)` |
| `Journals/JournalKind.cs`, `JournalRead.cs`, `JournalSnapshot.cs`, `JournalTally.cs` | unchanged except `JournalRead` loses `UnknownEntries` |
| `Neurons/INeuron.cs` | `Fire`, `Connect`, `Disconnect`, `Deliver` |
| `Neurons/INeuronQuery.cs` | `ReadState`, `ReadSynapses`, `ReadJournal` |
| `Neurons/NeuronCallTimeouts.cs` | unchanged |

Everything else in Contracts is deleted.

**Runtime (`src/Kernel/DigitalBrain`)** — after Task 1:

| File | Responsibility |
|---|---|
| `Neuron/Neuron.cs` | base grain: Fire/Connect/Disconnect/Deliver, journals, synapses, latest-per-type, virtual `ReceiveAsync` |
| `Neuron/NeuronOfState.cs` | `Neuron<TState>` snapshot facet |
| `Neuron/PlainNeuron.cs` | `[GrainType("neuron")]`, the default neuron |
| `Neuron/NeuronRuntime.cs` | binds durable collections per activation |
| `Neuron/NeuronSynapses.cs` | synapse dictionary |
| `Neuron/NeuronJournals.cs`, `JournalWindow.cs`, `JournalEntry.cs` | journals (watchers removed) |
| `Neuron/NeuronRequestPath.cs`, `NeuronConcurrency.cs` | unchanged guards |
| `Hosting/DigitalBrainRuntime.cs`, `ModuleManifest.cs`, `IModule.cs` | silo registration (broadcast channel and membrane filter removed) |
| `Serialization/ModelPayloadSerialization.cs`, `DurableStateJson.cs` | unchanged |

Deleted from the runtime: `Neuron/BrainNeuron.cs`, `SignalSender.cs`, `SignalDispatcher.cs`, `SignalRouter.cs`, `NeuronMembraneFilter.cs`, `JournalWindowCheckpoint.cs`, the `Entities/`, `Execution/`, `Identity/` folders, `SignalTelemetry.cs` if it exists under `Serialization/` or `Neuron/`.

**Mcp (`src/Kernel/DigitalBrain.Mcp`)** — new in Task 4/5:

| File | Responsibility |
|---|---|
| `BrainOperations.cs` | the four operations as plain C# |
| `Requests.cs` | request/result records |
| `BrainTools.cs` | `[McpServerToolType]` wrappers |
| `SessionPrincipal.cs` | resolves the caller's Session name |
| `DigitalBrainMcpHosting.cs` | `AddDigitalBrainMcp`, `MapDigitalBrainMcp` |

**Testing (`src/Testing/DigitalBrain.Testing`)**: `BrainSimulation.cs` (client removed), `FileJournalStorageProvider.cs`, `FileGrainStorage.cs` unchanged, `JournalWait.cs` rewritten over `INeuronQuery`.

**Tests (`tests/DigitalBrain.Tests`)**: `Features/*.feature`, `Features/BrainSteps.cs`, `Features/Fixtures.cs`, `Features/BrainWorld.cs`.

---

### Task 1: Cut Contracts, runtime, testing, and hosts down to the model (compiles, no tests yet)

This task is one atomic cut: the deletions form a single dependency graph, so nothing compiles until all of it is done. Work in the order below; do not run tests until step 11.

**Files:**
- Rewrite: `src/Kernel/DigitalBrain.Contracts/Identity/NeuronId.cs`, `Identity/IdentityPart.cs`, `Signals/Signal.cs`, `Signals/SignalDelivery.cs`, `Synapses/Synapse.cs`, `Journals/JournalRead.cs`, `Neurons/INeuron.cs`, `Neurons/INeuronQuery.cs`, `DigitalBrain.Contracts.csproj`
- Delete in Contracts: `Execution/`, `Entities/`, `Identity/ActorContext.cs`, `Identity/EntityId.cs`, `Identity/OwnerId.cs`, `Identity/PrincipalId.cs`, `Identity/PrincipalPartition.cs`, `Identity/GrainTypeNames.cs`, `Journals/IJournalObserver.cs`, `Journals/UnknownJournalEntry.cs`, `Neurons/IBrainNeuron.cs`, `Neurons/IHandle.cs`, `Neurons/INeuronGrain.cs`, `Neurons/NeuronAuthorizationException.cs`, `Signals/DeliveryOutcome.cs`, `Signals/DigitalBrainActivated.cs`, `Signals/IDeferredReply.cs`, `Signals/SignalDeliveryResult.cs`, `Signals/SignalOfResponse.cs`, `Signals/SignalRequestPolicy.cs`, `Signals/Subscribe.cs`, `Synapses/SynapseKind.cs`, `ChannelJournalObserver.cs`, `IDigitalBrain.cs`, `INeuronClient.cs`, `INeuronReference.cs`, `NeuronReference.cs`, `NeuronReferenceExtensions.cs`, `SignalDeliveryRefusedException.cs`
- Rewrite in runtime: `src/Kernel/DigitalBrain/Neuron/Neuron.cs`, `NeuronRuntime.cs`, `NeuronSynapses.cs`, `NeuronJournals.cs`, `JournalWindow.cs`, `Hosting/DigitalBrainRuntime.cs`, `DigitalBrain.csproj`
- Create: `src/Kernel/DigitalBrain/Neuron/PlainNeuron.cs`, `Neuron/NeuronOfState.cs`
- Delete in runtime: listed in the file map above
- Delete project: `src/Kernel/DigitalBrain.Sdk/` (entire folder)
- Modify: `src/Testing/DigitalBrain.Testing/BrainSimulation.cs`, `JournalWait.cs`, `DigitalBrain.Testing.csproj`
- Modify (forced): `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj`, `DigitalBrainRuntimeHostingExtensions.cs`; delete `DigitalBrainActivationHostedService.cs`, `DigitalBrainClientHostingExtensions.cs`
- Modify: `src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj`, `Program.cs`; move `Auth/KernelCors.cs` to `KernelCors.cs`; delete `Auth/BasicAuthGate.cs`
- Rename: `tests/DigitalBrain.Substrate.Tests/` → `tests/DigitalBrain.Tests/` (csproj too); delete all existing `Features/*` there
- Modify: `DigitalBrain.slnx`

**Interfaces produced (later tasks depend on these exact names):**

```csharp
// Contracts
namespace DigitalBrain.Abstractions.Identity;
public readonly record struct NeuronId(string Type, string Name) {
    public const string PlainType = "neuron";
    public GrainId ToGrainId();
    public static NeuronId Plain(string name);
    public static NeuronId FromGrainId(GrainId id);
    public static bool TryParse(string text, out NeuronId id); // "type:name" or "name" (=> plain)
    public override string ToString(); // "type:name"
}

namespace DigitalBrain.Abstractions.Signals;
public sealed record Signal(string Type, string Body) {
    public const int MaxBodyBytes = 65_536;
    public static Signal Create(string type, string body); // validates, throws SignalRejectedException
}
public sealed class SignalRejectedException(string message) : InvalidOperationException(message);
public sealed record SignalDelivery(Signal Signal, SignalId SignalId, CorrelationId CorrelationId,
    SignalId? CausationId, NeuronId Source, long Sequence, DateTimeOffset Timestamp);

namespace DigitalBrain.Abstractions.Synapses;
public readonly record struct Synapse(NeuronId Source, NeuronId Target, string SignalType, DateTimeOffset CreatedAt);

namespace DigitalBrain.Abstractions.Neurons;
public interface INeuron : IGrainWithStringKey {
    Task<int> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken ct = default);
    Task Connect(NeuronId target, string signalType);
    Task Disconnect(NeuronId target, string signalType);
    Task Deliver(SignalDelivery delivery, CancellationToken ct = default);
}
public interface INeuronQuery : IGrainWithStringKey {
    Task<IReadOnlyList<SignalDelivery>> ReadState();     // latest per type
    Task<IReadOnlyList<Synapse>> ReadSynapses();
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
}

// Runtime
namespace DigitalBrain.Core;
public abstract class Neuron : DurableGrain, INeuron, INeuronQuery {
    protected Neuron(NeuronRuntime runtime);
    public NeuronId Id { get; }
    protected virtual Task ReceiveAsync(SignalDelivery delivery, CancellationToken ct); // default no-op
    protected Task<int> FireAsync(Signal signal, NeuronId? to = null, CorrelationId? correlation = null, CancellationToken ct = default);
}
public abstract class Neuron<TState> : Neuron where TState : class {
    protected Neuron(NeuronRuntime runtime, IPersistentState<TState> state);
    protected TState? State { get; }
    protected Task SaveAsync(TState value, CancellationToken ct = default);
}
[GrainType("neuron")] public sealed class PlainNeuron(NeuronRuntime runtime) : Neuron(runtime);
```

- [ ] **Step 1: Delete the Sdk project and every Contracts file listed above**

```bash
cd D:/digitalbrain
git rm -r -q src/Kernel/DigitalBrain.Sdk
cd src/Kernel/DigitalBrain.Contracts
git rm -r -q Execution Entities ChannelJournalObserver.cs IDigitalBrain.cs INeuronClient.cs INeuronReference.cs NeuronReference.cs NeuronReferenceExtensions.cs SignalDeliveryRefusedException.cs \
  Identity/ActorContext.cs Identity/EntityId.cs Identity/OwnerId.cs Identity/PrincipalId.cs Identity/PrincipalPartition.cs Identity/GrainTypeNames.cs \
  Journals/IJournalObserver.cs Journals/UnknownJournalEntry.cs \
  Neurons/IBrainNeuron.cs Neurons/IHandle.cs Neurons/INeuronGrain.cs Neurons/NeuronAuthorizationException.cs \
  Signals/DeliveryOutcome.cs Signals/DigitalBrainActivated.cs Signals/IDeferredReply.cs Signals/SignalDeliveryResult.cs Signals/SignalOfResponse.cs Signals/SignalRequestPolicy.cs Signals/Subscribe.cs \
  Synapses/SynapseKind.cs
```

- [ ] **Step 2: Rewrite the Contracts records**

`Identity/IdentityPart.cs`:
```csharp
namespace DigitalBrain.Abstractions.Identity;

internal static class IdentityPart
{
    internal const int MaxLength = 128;

    // Neuron names and types: no whitespace, no ':' (it separates type from name), at most 128 chars.
    internal static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaxLength)
        {
            throw new ArgumentException($"'{value[..16]}…' is longer than {MaxLength} characters.", parameterName);
        }
        if (value.Contains(':', StringComparison.Ordinal) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"'{value}' cannot contain ':' or whitespace.", parameterName);
        }
        return value;
    }
}
```

`Identity/NeuronId.cs`:
```csharp
using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.neuron-id")]
public readonly record struct NeuronId
{
    public const string PlainType = "neuron";

    [JsonConstructor]
    public NeuronId(string type, string name)
    {
        Type = IdentityPart.Validated(type, nameof(type)).ToLowerInvariant();
        Name = IdentityPart.Validated(name, nameof(name));
    }

    [Id(0)] public string Type { get; }
    [Id(1)] public string Name { get; }

    public GrainId ToGrainId() => GrainId.Create(Type, Name);

    public static NeuronId Plain(string name) => new(PlainType, name);

    public static NeuronId FromGrainId(GrainId id) => new(id.Type.ToString()!, id.Key.ToString()!);

    // "type:name" or bare "name" (a plain neuron).
    public static bool TryParse(string? text, out NeuronId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        try
        {
            id = separator < 0
                ? Plain(trimmed)
                : new NeuronId(trimmed[..separator], trimmed[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override string ToString() => $"{Type}:{Name}";
}
```

`Signals/Signal.cs`:
```csharp
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DigitalBrain.Abstractions.Signals;

// A signal is a type name plus a JSON body. Core ships no vocabulary; callers invent it.
[GenerateSerializer]
[Alias("db.signal")]
public sealed partial record Signal
{
    public const int MaxBodyBytes = 65_536;
    public const int MaxTypeLength = 64;

    private Signal(string type, string body)
    {
        Type = type;
        Body = body;
    }

    [Id(0)] public string Type { get; }
    [Id(1)] public string Body { get; }

    public static Signal Create(string type, string body)
    {
        if (string.IsNullOrWhiteSpace(type) || !TypeName().IsMatch(type))
        {
            throw new SignalRejectedException(
                $"Signal type '{type}' is not vocabulary. Use letters only, such as 'Note'; put identity in the neuron name.");
        }

        body = string.IsNullOrWhiteSpace(body) ? "{}" : body;
        var bytes = Encoding.UTF8.GetByteCount(body);
        if (bytes > MaxBodyBytes)
        {
            throw new SignalRejectedException(
                $"Signal body is {bytes / 1024} KB; the limit is {MaxBodyBytes / 1024} KB. "
                + "Split the content across neurons or store it externally and fire a reference.");
        }

        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException error)
        {
            throw new SignalRejectedException($"Signal body is not valid JSON: {error.Message}");
        }

        return new Signal(type, body);
    }

    [GeneratedRegex("^[A-Za-z]{1,64}$")]
    private static partial Regex TypeName();
}

public sealed class SignalRejectedException(string message) : InvalidOperationException(message);
```

`Signals/SignalDelivery.cs`:
```csharp
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

// The envelope around a Signal. Identity, causation, correlation and source ride here.
[GenerateSerializer]
[Alias("db.signal-delivery")]
public sealed record SignalDelivery(
    [property: Id(0)] Signal Signal,
    [property: Id(1)] SignalId SignalId,
    [property: Id(2)] CorrelationId CorrelationId,
    [property: Id(3)] SignalId? CausationId,
    [property: Id(4)] NeuronId Source,
    [property: Id(5)] long Sequence,
    [property: Id(6)] DateTimeOffset Timestamp)
{
    public static SignalDelivery Create(
        Signal signal, NeuronId source, long sequence, TimeProvider clock,
        SignalDelivery? cause = null, CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return new(
            signal,
            SignalId.New(),
            correlation ?? cause?.CorrelationId ?? CorrelationId.New(),
            cause?.SignalId,
            source,
            sequence,
            clock.GetUtcNow());
    }
}
```

`Synapses/Synapse.cs`:
```csharp
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Synapses;

// A directed, typed edge stored on the SOURCE neuron. The only routing fact in the system.
[GenerateSerializer]
[Alias("db.synapse")]
public readonly record struct Synapse(
    [property: Id(0)] NeuronId Source,
    [property: Id(1)] NeuronId Target,
    [property: Id(2)] string SignalType,
    [property: Id(3)] DateTimeOffset CreatedAt)
{
    public override string ToString() => $"{Source} --{SignalType}--> {Target}";
}
```

`Journals/JournalRead.cs`:
```csharp
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Journals;

[GenerateSerializer]
[Alias("db.journal-read")]
public sealed record JournalRead(
    [property: Id(0)] long ResumeSequence,
    [property: Id(1)] IReadOnlyList<SignalDelivery> Delta,
    [property: Id(2)] JournalSnapshot? ResetSnapshot);
```

`Neurons/INeuron.cs`:
```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

// The two verbs, plus Deliver, which only another neuron's Fire calls.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    // to == null: along every synapse of signal.Type. to != null: along exactly that synapse,
    // creating it first if missing. Returns the number of neurons delivered to.
    [Alias(nameof(Fire))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<int> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default);

    [Alias(nameof(Connect))]
    Task Connect(NeuronId target, string signalType);

    [Alias(nameof(Disconnect))]
    Task Disconnect(NeuronId target, string signalType);

    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);
}
```

`Neurons/INeuronQuery.cs`:
```csharp
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// Read is a query: nothing moves, nothing is journaled.
[Alias("db.v3.neuron-query")]
public interface INeuronQuery : IGrainWithStringKey
{
    [ReadOnly] [AlwaysInterleave] [Alias(nameof(ReadState))]
    Task<IReadOnlyList<SignalDelivery>> ReadState();

    [ReadOnly] [AlwaysInterleave] [Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();

    [ReadOnly] [AlwaysInterleave] [Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
}
```

`DigitalBrain.Contracts.csproj`: replace the `InternalsVisibleTo` group with `DigitalBrain`, `DigitalBrain.Mcp`, `DigitalBrain.Testing`, `DigitalBrain.Tests`.

- [ ] **Step 3: Delete the runtime files that have no place in the model**

```bash
cd D:/digitalbrain/src/Kernel/DigitalBrain
git rm -r -q Entities Execution Identity Neuron/BrainNeuron.cs Neuron/SignalSender.cs Neuron/SignalDispatcher.cs Neuron/SignalRouter.cs Neuron/NeuronMembraneFilter.cs Neuron/JournalWindowCheckpoint.cs
grep -rl "SignalTelemetry" --include=*.cs . | grep -v obj   # delete the file that defines it, and every use
```

- [ ] **Step 4: Rewrite `NeuronSynapses.cs`**

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class NeuronSynapses(IDurableDictionary<string, Synapse> synapses, NeuronId source, TimeProvider clock)
{
    private static string KeyFor(NeuronId target, string signalType) => $"{target} {signalType}";

    internal IReadOnlyList<Synapse> All() => [.. synapses.Values];

    internal IReadOnlyList<Synapse> ForType(string signalType)
        => [.. synapses.Values.Where(s => string.Equals(s.SignalType, signalType, StringComparison.Ordinal))];

    internal bool Has(NeuronId target, string signalType) => synapses.ContainsKey(KeyFor(target, signalType));

    // Idempotent: connecting twice is one synapse.
    internal bool Connect(NeuronId target, string signalType)
    {
        var key = KeyFor(target, signalType);
        if (synapses.ContainsKey(key)) return false;
        synapses[key] = new Synapse(source, target, signalType, clock.GetUtcNow());
        return true;
    }

    // No-op when absent.
    internal bool Disconnect(NeuronId target, string signalType) => synapses.Remove(KeyFor(target, signalType));
}
```

- [ ] **Step 5: Trim `JournalWindow.cs` and `NeuronJournals.cs`**

In `JournalWindow.cs`: delete `HasUnresolvedTypeAt`, the `catch (FieldTypeMissingException)` branch (the deserialize call becomes a plain `deliveries.Add(...)`), `Checkpoint()`, `Restore(...)`, the `DigitalBrainActivatedTallyKey` field, and make `TallyKeyFor` return `delivery.Signal.Type`. The `Read` return becomes `new(lastSequence, deliveries, null)`. Remove the now-unused `using` lines for `Orleans.Serialization.WireProtocol` and `DigitalBrain.Abstractions`.

`NeuronJournals.cs` becomes:
```csharp
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed class NeuronJournals(JournalWindow incoming, JournalWindow outgoing)
{
    internal long OutgoingNextSequence => outgoing.NextSequence;
    internal JournalRead Read(JournalKind kind, long afterSequence) => WindowFor(kind).Read(afterSequence);
    internal void AppendIncoming(SignalDelivery delivery) => incoming.Append(delivery);
    internal void AppendOutgoing(SignalDelivery delivery) => outgoing.Append(delivery);

    private JournalWindow WindowFor(JournalKind kind) => kind switch
    {
        JournalKind.Incoming => incoming,
        JournalKind.Outgoing => outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
```

- [ ] **Step 6: Rewrite `NeuronRuntime.cs`**

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

public sealed class NeuronRuntime(TimeProvider clock)
{
    internal TimeProvider Clock { get; } = clock;

    internal NeuronActivationComponents Bind(IServiceProvider services, NeuronId neuronId)
    {
        var entries = services.GetRequiredService<Serializer<JournalEntry>>();
        var sessions = services.GetRequiredService<SerializerSessionPool>();
        JournalWindow Window(string name) => new(
            services.GetRequiredKeyedService<IDurableList<byte[]>>(name),
            services.GetRequiredKeyedService<IDurableDictionary<string, long>>($"{name}.tally"),
            services.GetRequiredKeyedService<IDurableValue<long>>($"{name}.sequence"),
            entries,
            sessions);

        return new(
            Clock,
            new NeuronJournals(Window("incoming"), Window("outgoing")),
            new NeuronSynapses(services.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>("synapses"), neuronId, Clock),
            services.GetRequiredKeyedService<IDurableDictionary<string, SignalDelivery>>("latest"));
    }
}

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    NeuronJournals Journals,
    NeuronSynapses Synapses,
    IDurableDictionary<string, SignalDelivery> Latest);
```

- [ ] **Step 7: Rewrite `Neuron.cs`, add `NeuronOfState.cs` and `PlainNeuron.cs`**

`Neuron/Neuron.cs`:
```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// A durable actor with one receive slot. Owns its synapses, two bounded journals, and the
// latest signal of each type it received. Fire travels along synapses; nothing else routes.
public abstract class Neuron : DurableGrain, INeuron, INeuronQuery
{
    private readonly NeuronActivationComponents _components;
    private SignalDelivery? _handling;

    protected Neuron(NeuronRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _components = runtime.Bind(ServiceProvider, Id);
    }

    public NeuronId Id => NeuronId.FromGrainId(this.GetGrainId());

    protected TimeProvider TimeProvider => _components.Clock;

    // The delivery this turn is reacting to, or null outside Deliver.
    protected SignalDelivery? CurrentDelivery => _handling;

    public sealed override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        NeuronConcurrency.RequireSerializedTurns(GetType());
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
        await OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);
    }

    protected virtual Task OnNeuronActivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Override to react. The default neuron does nothing: the signal is already journaled and remembered.
    protected virtual Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken) => Task.CompletedTask;

    // ---- INeuron ----

    public Task<int> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default)
        => FireAsync(signal, to, correlation, cancellationToken);

    public async Task Connect(NeuronId target, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Connect(target, signalType))
        {
            await WriteStateAsync().ConfigureAwait(true);
        }
    }

    public async Task Disconnect(NeuronId target, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (_components.Synapses.Disconnect(target, signalType))
        {
            await WriteStateAsync().ConfigureAwait(true);
        }
    }

    public async Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        _components.Journals.AppendIncoming(delivery);
        _components.Latest[delivery.Signal.Type] = delivery;
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        var previous = _handling;
        _handling = delivery;
        try
        {
            await ReceiveAsync(delivery, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _handling = previous;
        }
    }

    // ---- INeuronQuery ----

    public Task<IReadOnlyList<SignalDelivery>> ReadState()
        => Task.FromResult<IReadOnlyList<SignalDelivery>>([.. _components.Latest.Values.OrderBy(d => d.Signal.Type, StringComparer.Ordinal)]);

    public Task<IReadOnlyList<Synapse>> ReadSynapses() => Task.FromResult(_components.Synapses.All());

    public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
        => Task.FromResult(_components.Journals.Read(kind, afterSequence));

    // ---- for subclasses ----

    protected async Task<int> FireAsync(Signal signal, NeuronId? to = null, CorrelationId? correlation = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        // Re-validate: a Signal deserialized from the wire may bypass Create.
        _ = Signal.Create(signal.Type, signal.Body);

        if (to is { } target && target == Id)
        {
            throw new SignalRejectedException($"Neuron '{Id}' cannot fire at itself.");
        }

        if (to is { } single && _components.Synapses.Connect(single, signal.Type))
        {
            // The directed edge is created before the journal entry so anatomy and traffic agree.
        }

        var delivery = SignalDelivery.Create(signal, Id, _components.Journals.OutgoingNextSequence, TimeProvider, _handling, correlation);
        _components.Journals.AppendOutgoing(delivery);
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        var targets = to is { } one
            ? [one]
            : _components.Synapses.ForType(signal.Type).Select(s => s.Target).Where(t => t != Id).Distinct().ToArray();

        List<Exception>? failures = null;
        foreach (var receiver in targets)
        {
            try
            {
                using var path = NeuronRequestPath.Enter(Id, receiver);
                await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId())
                    .Deliver(delivery, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException($"Delivery of '{signal.Type}' from '{Id}' failed for {failures.Count} of {targets.Length} receivers.", failures);
        }

        return targets.Length;
    }

    protected new IDisposable RegisterTimer(Func<object, Task> callback, object state, TimeSpan dueTime, TimeSpan period)
        => throw new InvalidOperationException($"{nameof(RegisterTimer)} creates interleaving callbacks, but neurons require serialized turns.");
}
```

`Neuron/NeuronOfState.cs`:
```csharp
using DigitalBrain.Abstractions;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// A neuron whose domain state is a snapshot (IPersistentState) rather than the op log.
// Concrete subclasses must redeclare [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)]
// on their own constructor parameter and forward it: Orleans binds facets on the leaf constructor.
public abstract class Neuron<TState> : Neuron where TState : class
{
    private readonly IPersistentState<TState> _state;

    protected Neuron(NeuronRuntime runtime, IPersistentState<TState> state) : base(runtime)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    protected TState? State => _state.RecordExists ? _state.State : null;

    protected async Task SaveAsync(TState value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        _state.State = value;
        await _state.WriteStateAsync(cancellationToken).ConfigureAwait(true);
    }
}
```

`Neuron/PlainNeuron.cs`:
```csharp
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

// The default neuron: a name, synapses, journals, and the latest signal per type. Reacts to nothing.
[GrainType(NeuronId.PlainType)]
public sealed class PlainNeuron(NeuronRuntime runtime) : Neuron(runtime);
```

`NeuronConcurrency.cs`: it references `INeuronQuery`; keep. `NeuronRequestPath.cs`: unchanged.

- [ ] **Step 8: Trim hosting**

`Hosting/DigitalBrainRuntime.cs`: remove the `AddBroadcastChannel(...)` call and the `AddIncomingGrainCallFilter<NeuronMembraneFilter>()` line; remove `TryAddSingleton<SignalRouter>()`; add `builder.AddActivityPropagation();` after `AddJournalStorage()`. Keep module hooks.

`DigitalBrain.csproj`: remove the `Microsoft.Orleans.BroadcastChannel` package reference; replace `InternalsVisibleTo` entries with `DigitalBrain.Tests`, `DigitalBrain.Testing`, `DigitalBrain.Mcp`.

- [ ] **Step 9: The three forced Aspire edits**

1. `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj`: delete the `ProjectReference` to `DigitalBrain.Sdk`.
2. `git rm src/Aspire/DigitalBrain.Aspire/DigitalBrainActivationHostedService.cs src/Aspire/DigitalBrain.Aspire/DigitalBrainClientHostingExtensions.cs`
3. `DigitalBrainRuntimeHostingExtensions.cs`: delete the line `builder.AddDigitalBrainOwner(activateOnStart: false);`.

Then `grep -rn "AddDigitalBrainClient\|AddDigitalBrainOwner\|IDigitalBrain\b" src/Aspire --include=*.cs | grep -v obj` must print nothing.

- [ ] **Step 10: Silo, Testing, tests project, solution**

Silo: `git rm src/Kernel/DigitalBrain.Silo/Auth/BasicAuthGate.cs`; `git mv src/Kernel/DigitalBrain.Silo/Auth/KernelCors.cs src/Kernel/DigitalBrain.Silo/KernelCors.cs` and change its namespace to `DigitalBrain.Kernel`; in `Program.cs` remove `using DigitalBrain.Kernel.Auth;`; in the csproj remove the `DigitalBrain.Sdk` reference.

Testing `BrainSimulation.cs`: delete the `Brain` property, both `DigitalBrainClient.Connect` calls, the `BrainFor` method and the `using DigitalBrain.Abstractions;` line. Testing csproj: remove the `DigitalBrain.Sdk` reference. `JournalWait.cs`: delete the two `ForAsync` overloads that take `NeuronReference<TNeuron>` and `IDigitalBrain`, keep the `INeuronQuery` overload and the private core; delete unused usings.

Tests: `git mv tests/DigitalBrain.Substrate.Tests tests/DigitalBrain.Tests`, `git mv tests/DigitalBrain.Tests/DigitalBrain.Substrate.Tests.csproj tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`, `git rm -r tests/DigitalBrain.Tests/Features/*`. In the csproj replace the `DigitalBrain.Sdk` reference with `../../src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj` **only after Task 4 creates it**; for now delete the Sdk line. Add `<RootNamespace>DigitalBrain.Tests</RootNamespace>`.

Create `tests/DigitalBrain.Tests/Features/BrainWorld.cs`:
```csharp
using DigitalBrain.Testing;

namespace DigitalBrain.Tests;

public sealed class BrainWorld
{
    public BrainSimulation? Simulation { get; set; }
    public BrainSimulation Brain => Simulation ?? throw new InvalidOperationException("Given a running brain first.");
}
```

`DigitalBrain.slnx`: remove the `DigitalBrain.Sdk` project line; change the tests path to `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`.

- [ ] **Step 11: Build until clean**

Run: `dotnet build DigitalBrain.slnx`
Expected: 0 errors, 0 warnings. Fix any leftover reference to a deleted type by deleting the caller, never by re-adding the type. Then `dotnet test tests/DigitalBrain.Tests` runs zero tests and passes.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "refactor: cut kernel to Neuron/Synapse, Fire/Connect, Read

Deletes IHandle, IContext, Entity, owner/principal identity, learned
synapses, Watch, the Sdk project and the old feature suite per
docs/architecture/01-04."
```

---

### Task 2: `fire` and `connect` features

**Files:**
- Create: `tests/DigitalBrain.Tests/Features/fire.feature`, `connect.feature`, `Features/BrainSteps.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` (nothing new yet; Reqnroll picks features up automatically)

**Interfaces:** consumes `INeuron`, `INeuronQuery`, `NeuronId`, `Signal` from Task 1. Produces the step vocabulary reused by Tasks 3 and 4.

- [ ] **Step 1: Write `fire.feature`**

```gherkin
Feature: Fire
  A neuron emits a signal. It travels along every synapse of that signal type on the
  emitter, or along exactly one synapse when a target is named. Nothing else routes.

  Rule: Fire without a target follows every synapse of that type

    Scenario: Two connected neurons receive, an unconnected one does not
      Given a running brain
      And "elon" is connected to "alice" for "Post"
      And "elon" is connected to "bob" for "Post"
      When "elon" fires "Post" {"text":"starship"}
      Then the fire reached 2 neurons
      And "alice" incoming journal contains "Post" {"text":"starship"}
      And "bob" incoming journal contains "Post" {"text":"starship"}
      And "carol" incoming journal is empty

    Scenario: Synapses of another type do not carry the signal
      Given a running brain
      And "elon" is connected to "alice" for "Post"
      When "elon" fires "Note" {"text":"private"}
      Then the fire reached 0 neurons
      And "alice" incoming journal is empty

  Rule: Fire with a target follows exactly that synapse and creates it

    Scenario: A directed fire reaches only the target and leaves the synapse behind
      Given a running brain
      And "elon" is connected to "alice" for "Post"
      When "elon" fires "Post" {"text":"hi bob"} at "bob"
      Then the fire reached 1 neurons
      And "bob" incoming journal contains "Post" {"text":"hi bob"}
      And "alice" incoming journal is empty
      And "elon" has a synapse to "bob" for "Post"

  Rule: The emitter never receives its own fire

    Scenario: A neuron connected to itself is skipped
      Given a running brain
      And "elon" is connected to "elon" for "Post"
      When "elon" fires "Post" {"text":"echo"}
      Then the fire reached 0 neurons
      And "elon" incoming journal is empty

    Scenario: A directed fire at itself is rejected
      Given a running brain
      When "elon" fires "Post" {"text":"me"} at "elon"
      Then the fire was rejected with a message containing "cannot fire at itself"

  Rule: Both ends journal the envelope

    Scenario: The outgoing and incoming entries share signal id and correlation
      Given a running brain
      When "elon" fires "Post" {"text":"one"} at "alice"
      Then "elon" outgoing journal has 1 entries
      And "alice" incoming journal has 1 entries
      And the latest "alice" incoming entry has source "elon"
      And the latest "alice" incoming entry has the same signal id and correlation as the latest "elon" outgoing entry
```

- [ ] **Step 2: Write `connect.feature`**

```gherkin
Feature: Connect
  Connect creates a synapse on the source. Disconnect removes it. Neurons exist when named.

  Scenario: Connect twice is one synapse
    Given a running brain
    When "git" is connected to "run-tests" for "Note"
    And "git" is connected to "run-tests" for "Note"
    Then "git" has 1 synapses

  Scenario: Disconnect removes the synapse and a second disconnect is a no-op
    Given a running brain
    And "git" is connected to "run-tests" for "Note"
    When "git" is disconnected from "run-tests" for "Note"
    And "git" is disconnected from "run-tests" for "Note"
    Then "git" has 0 synapses

  Scenario: After disconnect a fire no longer reaches the old target
    Given a running brain
    And "git" is connected to "run-tests" for "Note"
    And "git" is disconnected from "run-tests" for "Note"
    When "git" fires "Note" {"text":"gone"}
    Then the fire reached 0 neurons
    And "run-tests" incoming journal is empty

  Scenario: A neuron that was only ever named reads as empty
    Given a running brain
    Then "never-touched" has 0 synapses
    And "never-touched" incoming journal is empty
    And "never-touched" state is empty
```

- [ ] **Step 3: Write `BrainSteps.cs`**

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class BrainSteps(BrainWorld world)
{
    private int _lastCount;
    private Exception? _lastError;

    [Given("a running brain")]
    public async Task GivenARunningBrain()
        => world.Simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });

    [Given("a running brain with durable storage")]
    public async Task GivenADurableBrain()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            PersistenceDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N")),
        });

    [Given(@"""(.*)"" is connected to ""(.*)"" for ""(.*)""")]
    [When(@"""(.*)"" is connected to ""(.*)"" for ""(.*)""")]
    public Task Connect(string from, string to, string type) => Neuron(from).Connect(Id(to), type);

    [Given(@"""(.*)"" is disconnected from ""(.*)"" for ""(.*)""")]
    [When(@"""(.*)"" is disconnected from ""(.*)"" for ""(.*)""")]
    public Task Disconnect(string from, string to, string type) => Neuron(from).Disconnect(Id(to), type);

    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\})$")]
    public Task Fire(string from, string type, string body) => FireCore(from, type, body, to: null);

    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at ""(.*)""")]
    public Task FireAt(string from, string type, string body, string to) => FireCore(from, type, body, to);

    [When("the silo restarts")]
    public Task Restart() => Brain.RestartSiloAsync();

    [Then(@"the fire reached (\d+) neurons")]
    public void ThenReached(int count)
    {
        Assert.Null(_lastError);
        Assert.Equal(count, _lastCount);
    }

    [Then(@"the fire was rejected with a message containing ""(.*)""")]
    public void ThenRejected(string fragment)
    {
        Assert.NotNull(_lastError);
        Assert.Contains(fragment, Flatten(_lastError).Message, StringComparison.Ordinal);
    }

    [Then(@"""(.*)"" incoming journal contains ""(\w+)"" (\{.*\})$")]
    public async Task ThenIncomingContains(string name, string type, string body)
        => Assert.Contains((await Journal(name, JournalKind.Incoming)).Delta, d => d.Signal.Type == type && d.Signal.Body == body);

    [Then(@"""(.*)"" incoming journal is empty")]
    public async Task ThenIncomingEmpty(string name) => Assert.Empty((await Journal(name, JournalKind.Incoming)).Delta);

    [Then(@"""(.*)"" (incoming|outgoing) journal has (\d+) entries")]
    public async Task ThenJournalCount(string name, string kind, int count)
        => Assert.Equal(count, (await Journal(name, Kind(kind))).Delta.Count);

    [Then(@"the latest ""(.*)"" incoming entry has source ""(.*)""")]
    public async Task ThenLatestSource(string name, string source)
        => Assert.Equal(Id(source), (await Journal(name, JournalKind.Incoming)).Delta[^1].Source);

    [Then(@"the latest ""(.*)"" incoming entry has the same signal id and correlation as the latest ""(.*)"" outgoing entry")]
    public async Task ThenSameEnvelope(string receiver, string source)
    {
        var incoming = (await Journal(receiver, JournalKind.Incoming)).Delta[^1];
        var outgoing = (await Journal(source, JournalKind.Outgoing)).Delta[^1];
        Assert.Equal(outgoing.SignalId, incoming.SignalId);
        Assert.Equal(outgoing.CorrelationId, incoming.CorrelationId);
    }

    [Then(@"""(.*)"" has a synapse to ""(.*)"" for ""(.*)""")]
    public async Task ThenHasSynapse(string from, string to, string type)
        => Assert.Contains(await Query(from).ReadSynapses(), s => s.Target == Id(to) && s.SignalType == type);

    [Then(@"""(.*)"" has (\d+) synapses")]
    public async Task ThenSynapseCount(string name, int count) => Assert.Equal(count, (await Query(name).ReadSynapses()).Count);

    [Then(@"""(.*)"" state is empty")]
    public async Task ThenStateEmpty(string name) => Assert.Empty(await Query(name).ReadState());

    [AfterScenario]
    public async Task AfterScenario()
    {
        if (world.Simulation is { } simulation)
        {
            await simulation.DisposeAsync();
            world.Simulation = null;
        }
    }

    // ---- helpers shared with later features ----

    internal BrainSimulation Brain => world.Brain;
    internal static NeuronId Id(string name) => NeuronId.Plain(name);
    internal INeuron Neuron(string name) => Brain.Grains.GetGrain<INeuron>(Id(name).ToGrainId());
    internal INeuronQuery Query(string name) => Brain.Grains.GetGrain<INeuronQuery>(Id(name).ToGrainId());
    internal Task<JournalRead> Journal(string name, JournalKind kind) => Query(name).ReadJournal(kind, 0);
    internal static JournalKind Kind(string text) => text == "incoming" ? JournalKind.Incoming : JournalKind.Outgoing;

    internal async Task FireCore(string from, string type, string body, string? to)
    {
        _lastError = null;
        try
        {
            _lastCount = await Neuron(from).Fire(Signal.Create(type, body), to is null ? null : Id(to), null);
        }
        catch (Exception error)
        {
            _lastError = error;
        }
    }

    internal void RecordError(Exception error) => _lastError = error;

    private static Exception Flatten(Exception error) => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions[0] : error;
}
```

- [ ] **Step 4: Run and make green**

Run: `dotnet test tests/DigitalBrain.Tests`
Expected on first run: the "rejected" scenario may fail if `SignalRejectedException` is wrapped by Orleans; `Flatten` handles `AggregateException`. If the message arrives inside an Orleans remoting exception, unwrap `InnerException` chains in `Flatten` until the message matches. All 10 scenarios green.

- [ ] **Step 5: Commit**

```bash
git add tests/DigitalBrain.Tests
git commit -m "test: fire and connect features"
```

---

### Task 3: `journal` and `state` features (latest-per-type, snapshot neuron, restart)

**Files:**
- Create: `tests/DigitalBrain.Tests/Features/journal.feature`, `state.feature`, `Features/Fixtures.cs`, `Features/StateSteps.cs`

**Interfaces:**
- Consumes `Neuron<TState>`, `SaveAsync`, `State`, `ReceiveAsync`, `CurrentDelivery` from Task 1.
- Produces fixture grain `IProfile` (`[GrainType("profile")]`) used to prove the snapshot path.

- [ ] **Step 1: Write `Fixtures.cs`**

```csharp
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Tests;

[GenerateSerializer]
[Alias("db.test.profile-state")]
public sealed record ProfileState([property: Id(0)] string Bio);

[Alias("db.test.profile")]
public interface IProfile : IGrainWithStringKey
{
    [Alias(nameof(ReadBio))]
    Task<string?> ReadBio();
}

// A Neuron<TState>: reacts to "SetBio" {"bio":"..."} by saving a snapshot. Everything else is ignored.
[GrainType("profile")]
internal sealed class Profile(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ProfileState> state)
    : Neuron<ProfileState>(runtime, state), IProfile
{
    public Task<string?> ReadBio() => Task.FromResult(State?.Bio);

    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "SetBio") return Task.CompletedTask;
        using var body = JsonDocument.Parse(delivery.Signal.Body);
        return SaveAsync(new ProfileState(body.RootElement.GetProperty("bio").GetString() ?? ""), cancellationToken);
    }
}
```

- [ ] **Step 2: Write `journal.feature`**

```gherkin
Feature: Journal
  Two bounded windows per neuron. Envelopes only. Tallies and sequences outlive the window.

  Scenario: Tallies count every delivery per type and survive restart
    Given a running brain with durable storage
    When "elon" fires "Post" {"n":1} at "alice"
    And "elon" fires "Post" {"n":2} at "alice"
    And "elon" fires "Note" {"n":3} at "alice"
    And the silo restarts
    Then "alice" incoming tally for "Post" is 2
    And "alice" incoming tally for "Note" is 1
    And "alice" incoming journal has 3 entries

  Scenario: The window drops the oldest entries but keeps the sequence
    Given a running brain
    When "elon" fires "Tick" {} at "alice" 520 times
    Then "alice" incoming journal has 512 entries
    And "alice" incoming last sequence is 520
    And "alice" incoming tally for "Tick" is 520

  Scenario: Reading a journal is not traffic
    Given a running brain
    When "elon" fires "Post" {"n":1} at "alice"
    And "alice" incoming journal is read 3 times
    Then "alice" incoming journal has 1 entries
    And "alice" outgoing journal has 0 entries
    And "elon" outgoing journal has 1 entries
```

- [ ] **Step 3: Write `state.feature`**

```gherkin
Feature: State
  A neuron remembers the latest signal of each type, durably, outside the window.
  A Neuron<TState> may additionally keep a snapshot; it never appears in a journal.

  Scenario: Firing a signal makes it the latest of its type
    Given a running brain
    When "claude" fires "Note" {"text":"run tests before commit"} at "run-tests"
    Then "run-tests" latest "Note" is {"text":"run tests before commit"}

  Scenario: A second signal of the same type replaces the latest and the journal keeps both
    Given a running brain
    When "claude" fires "Note" {"text":"v1"} at "run-tests"
    And "claude" fires "Note" {"text":"v2"} at "run-tests"
    Then "run-tests" latest "Note" is {"text":"v2"}
    And "run-tests" incoming journal contains "Note" {"text":"v1"}
    And "run-tests" state has 1 entries

  Scenario: Different types are remembered side by side
    Given a running brain
    When "claude" fires "Note" {"text":"x"} at "run-tests"
    And "claude" fires "Confirmed" {} at "run-tests"
    Then "run-tests" state has 2 entries
    And "run-tests" incoming tally for "Confirmed" is 1

  Scenario: Latest survives restart even after the window turned over
    Given a running brain with durable storage
    When "claude" fires "Note" {"text":"keep me"} at "run-tests"
    And "claude" fires "Tick" {} at "run-tests" 600 times
    And the silo restarts
    Then "run-tests" latest "Note" is {"text":"keep me"}

  Scenario: A snapshot neuron saves on receive and the snapshot is not in any journal
    Given a running brain with durable storage
    When "claude" fires "SetBio" {"bio":"mars"} at profile "elon"
    And the silo restarts
    Then profile "elon" bio is "mars"
    And profile "elon" incoming journal contains "SetBio" {"bio":"mars"}
    And profile "elon" outgoing journal has 0 entries
```

- [ ] **Step 4: Write `StateSteps.cs`**

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class StateSteps(BrainSteps brain)
{
    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at ""(.*)"" (\d+) times")]
    public async Task FireMany(string from, string type, string body, string to, int times)
    {
        for (var i = 0; i < times; i++) await brain.FireCore(from, type, body, to);
    }

    [When(@"""(.*)"" fires ""(\w+)"" (\{.*\}) at profile ""(.*)""")]
    public Task FireAtProfile(string from, string type, string body, string profile)
        => brain.Neuron(from).Fire(Signal.Create(type, body), ProfileId(profile), null);

    [When(@"""(.*)"" incoming journal is read (\d+) times")]
    public async Task ReadMany(string name, int times)
    {
        for (var i = 0; i < times; i++) _ = await brain.Journal(name, JournalKind.Incoming);
    }

    [Then(@"""(.*)"" (incoming|outgoing) tally for ""(\w+)"" is (\d+)")]
    public async Task ThenTally(string name, string kind, string type, long count)
    {
        var snapshot = await Snapshot(brain.Query(name), BrainSteps.Kind(kind));
        Assert.Equal(count, snapshot.Tallies.SingleOrDefault(t => t.SignalType == type)?.Recorded ?? 0);
    }

    [Then(@"""(.*)"" incoming last sequence is (\d+)")]
    public async Task ThenLastSequence(string name, long sequence)
        => Assert.Equal(sequence, (await Snapshot(brain.Query(name), JournalKind.Incoming)).LastSequence);

    [Then(@"""(.*)"" latest ""(\w+)"" is (\{.*\})$")]
    public async Task ThenLatest(string name, string type, string body)
        => Assert.Equal(body, (await brain.Query(name).ReadState()).Single(d => d.Signal.Type == type).Signal.Body);

    [Then(@"""(.*)"" state has (\d+) entries")]
    public async Task ThenStateCount(string name, int count) => Assert.Equal(count, (await brain.Query(name).ReadState()).Count);

    [Then(@"profile ""(.*)"" bio is ""(.*)""")]
    public async Task ThenBio(string profile, string bio)
        => Assert.Equal(bio, await brain.Brain.Grains.GetGrain<IProfile>(ProfileId(profile).ToGrainId()).ReadBio());

    [Then(@"profile ""(.*)"" incoming journal contains ""(\w+)"" (\{.*\})$")]
    public async Task ThenProfileIncoming(string profile, string type, string body)
        => Assert.Contains((await ProfileQuery(profile).ReadJournal(JournalKind.Incoming, 0)).Delta, d => d.Signal.Type == type && d.Signal.Body == body);

    [Then(@"profile ""(.*)"" outgoing journal has (\d+) entries")]
    public async Task ThenProfileOutgoing(string profile, int count)
        => Assert.Equal(count, (await ProfileQuery(profile).ReadJournal(JournalKind.Outgoing, 0)).Delta.Count);

    private static NeuronId ProfileId(string name) => new("profile", name);
    private INeuronQuery ProfileQuery(string name) => brain.Brain.Grains.GetGrain<INeuronQuery>(ProfileId(name).ToGrainId());

    // Read past the tip to get the snapshot without a delta.
    private static async Task<JournalSnapshot> Snapshot(INeuronQuery query, JournalKind kind)
    {
        var tip = await query.ReadJournal(kind, 0);
        var reset = await query.ReadJournal(kind, tip.ResumeSequence + 1);
        return reset.ResetSnapshot ?? throw new InvalidOperationException("Expected a snapshot past the tip.");
    }
}
```

Reqnroll injects `BrainSteps` into `StateSteps` via context injection; `BrainSteps` is `sealed` and `[Binding]`, which Reqnroll allows.

- [ ] **Step 5: Run and make green**

Run: `dotnet test tests/DigitalBrain.Tests`
Expected failures to fix in the runtime, in this order:
1. If `ReadJournal(kind, tip+1)` does not return a `ResetSnapshot`, check `JournalWindow.Read`: the `afterSequence > lastSequence` branch must return `new(lastSequence, [], Snapshot())`. It already does in the current code; keep it.
2. The 520-times scenario takes a few seconds; acceptable.
3. If `latest` survives but the profile snapshot does not, the test cluster's `FileGrainStorage` is not registered for durable runs. `BrainSimulation.ConfigureSilo` already registers it when `PersistenceDirectory` is set.

All scenarios from Tasks 2 and 3 green.

- [ ] **Step 6: Commit**

```bash
git add tests/DigitalBrain.Tests
git commit -m "test: journal and state features, snapshot neuron fixture"
```

---

### Task 4: `DigitalBrain.Mcp` operations and the `membrane` + `read` features

**Files:**
- Create: `src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj`, `Requests.cs`, `BrainOperations.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` (reference Mcp), `DigitalBrain.slnx` (add Mcp)
- Create: `tests/DigitalBrain.Tests/Features/membrane.feature`, `read.feature`, `Features/OperationSteps.cs`

**Interfaces produced:**

```csharp
namespace DigitalBrain.Mcp;
public sealed record FireRequest(string Type, string Body, string? To = null, string? Correlation = null);
public sealed record FireResult(string SignalId, string Correlation, int Delivered);
public sealed record ConnectRequest(string From, string To, string Type);
public sealed record ReadRequest(string Neuron, string? What = null, long After = 0, int TimeoutSeconds = 0);
public sealed record StateEntry(string Type, string Body, string From, DateTimeOffset At);
public sealed record SynapseEntry(string From, string To, string Type);
public sealed record JournalEntryView(long Sequence, string Type, string Body, string From, string SignalId, string Correlation, DateTimeOffset At);
public sealed record JournalView(long ResumeSequence, IReadOnlyList<JournalEntryView> Entries, long TotalRecorded);
public sealed record ReadResult(string Neuron, IReadOnlyList<StateEntry>? State, IReadOnlyList<SynapseEntry>? Synapses, JournalView? Incoming, JournalView? Outgoing);

public sealed class BrainOperations(IGrainFactory grains)
{
    public Task<FireResult> FireAsync(string session, FireRequest request, CancellationToken ct = default);
    public Task ConnectAsync(ConnectRequest request, CancellationToken ct = default);
    public Task DisconnectAsync(ConnectRequest request, CancellationToken ct = default);
    public Task<ReadResult> ReadAsync(ReadRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 1: Create the project**

`src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Description>DigitalBrain client: fire, connect, disconnect, read — as C# and as MCP tools.</Description>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol.AspNetCore" />
    <PackageReference Include="Microsoft.Orleans.Client" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../DigitalBrain.Contracts/DigitalBrain.Contracts.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="DigitalBrain.Tests" />
  </ItemGroup>
</Project>
```

Add `<Project Path="src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj" />` under the `/Modules/DigitalBrain/` folder in `DigitalBrain.slnx`. Add `<ProjectReference Include="../../src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj" />` to the tests csproj.

- [ ] **Step 2: Write `Requests.cs`** (the records above, verbatim, in `namespace DigitalBrain.Mcp;`).

- [ ] **Step 3: Write the failing `membrane.feature` and `read.feature`**

`membrane.feature`:
```gherkin
Feature: Membrane
  A signal is rejected before delivery when its body exceeds 64 KB or its type is not vocabulary.
  A rejected fire leaves no trace on either end.

  Scenario: A body over 64 KB is rejected with advice
    Given a running brain
    When session "claude" fires "Note" with a body of 70000 bytes at "big"
    Then the operation failed with a message containing "limit is 64 KB"
    And "big" incoming journal is empty
    And "claude" outgoing journal has 0 entries

  Scenario: A type with digits or spaces is rejected with advice
    Given a running brain
    When session "claude" fires "note-2026-09-08" {"text":"x"} at "dated"
    Then the operation failed with a message containing "letters only"
    And "dated" incoming journal is empty

  Scenario: A body that is not JSON is rejected
    Given a running brain
    When session "claude" fires "Note" not-json at "bad"
    Then the operation failed with a message containing "not valid JSON"

  Scenario: An empty body is allowed and stored as an empty object
    Given a running brain
    When session "claude" fires "Confirmed" with an empty body at "run-tests"
    Then "run-tests" latest "Confirmed" is {}
```

`read.feature`:
```gherkin
Feature: Read
  Read is a query over state, synapses and journals. It changes nothing. With a timeout it
  waits for the next journal entry. The caller is a Session neuron named by its principal.

  Scenario: A directed fire from a session leaves a synapse and both journals
    Given a running brain
    When session "claude" fires "Note" {"text":"run tests"} at "run-tests"
    Then the fire result reports 1 delivered
    And reading "claude" shows a synapse to "run-tests" for "Note"
    And reading "run-tests" shows latest "Note" {"text":"run tests"}

  Scenario: The default read returns all four views
    Given a running brain
    When session "claude" fires "Note" {"text":"x"} at "run-tests"
    And "run-tests" is read
    Then the read has state, synapses, incoming and outgoing

  Scenario: A partial read returns only what was asked
    Given a running brain
    When "run-tests" is read for "synapses"
    Then the read has only synapses

  Scenario: Reading changes nothing
    Given a running brain
    When session "claude" fires "Note" {"text":"x"} at "run-tests"
    And "run-tests" is read 5 times
    And "claude" is read 5 times
    Then "run-tests" incoming journal has 1 entries
    And "claude" outgoing journal has 1 entries
    And "claude" has 1 synapses

  Scenario: A walk from a topic reaches exactly its targets
    Given a running brain
    And "git" is connected to "run-tests" for "Note"
    And "git" is connected to "small-commits" for "Note"
    And "git" is connected to "unrelated" for "Other"
    When the synapses of "git" for "Note" are followed
    Then the walk visited "run-tests, small-commits"

  Scenario: A read with timeout returns when a new entry arrives
    Given a running brain
    When "claude" incoming is read after 0 with a 5 second timeout while "elon" fires "Ping" {} at "claude" after 300 ms
    Then the timed read returned 1 entries

  Scenario: A read with timeout returns empty at the deadline
    Given a running brain
    When "claude" incoming is read after 0 with a 1 second timeout
    Then the timed read returned 0 entries

  Scenario: Two connections with the same principal share one Session neuron
    Given a running brain
    When session "claude" fires "Note" {"text":"a"} at "one"
    And session "claude" fires "Note" {"text":"b"} at "two"
    Then "claude" outgoing journal has 2 entries
    And "claude" has 2 synapses
```

- [ ] **Step 4: Write `OperationSteps.cs`**

```csharp
using System.Diagnostics;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Mcp;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class OperationSteps(BrainSteps brain)
{
    private FireResult? _fire;
    private ReadResult? _read;
    private JournalView? _timed;
    private Exception? _error;
    private List<string> _walk = [];

    private BrainOperations Ops => new(brain.Brain.Grains);

    [When(@"session ""(.*)"" fires ""([^""]+)"" (\{.*\}) at ""(.*)""")]
    public Task SessionFires(string session, string type, string body, string to) => Try(() => Ops.FireAsync(session, new(type, body, to)));

    [When(@"session ""(.*)"" fires ""(\w+)"" with a body of (\d+) bytes at ""(.*)""")]
    public Task SessionFiresBig(string session, string type, int bytes, string to)
        => Try(() => Ops.FireAsync(session, new(type, "{\"t\":\"" + new string('x', bytes - 8) + "\"}", to)));

    [When(@"session ""(.*)"" fires ""(\w+)"" not-json at ""(.*)""")]
    public Task SessionFiresNotJson(string session, string type, string to) => Try(() => Ops.FireAsync(session, new(type, "not json", to)));

    [When(@"session ""(.*)"" fires ""(\w+)"" with an empty body at ""(.*)""")]
    public Task SessionFiresEmpty(string session, string type, string to) => Try(() => Ops.FireAsync(session, new(type, "", to)));

    [When(@"""(.*)"" is read")]
    public async Task Read(string neuron) => _read = await Ops.ReadAsync(new(neuron));

    [When(@"""(.*)"" is read for ""(\w+)""")]
    public async Task ReadFor(string neuron, string what) => _read = await Ops.ReadAsync(new(neuron, what));

    [When(@"""(.*)"" is read (\d+) times")]
    public async Task ReadMany(string neuron, int times)
    {
        for (var i = 0; i < times; i++) _read = await Ops.ReadAsync(new(neuron));
    }

    [When(@"the synapses of ""(.*)"" for ""(\w+)"" are followed")]
    public async Task Walk(string topic, string type)
    {
        var read = await Ops.ReadAsync(new(topic, "synapses"));
        _walk = [.. read.Synapses!.Where(s => s.Type == type).Select(s => s.To).Order(StringComparer.Ordinal)];
    }

    [When(@"""(.*)"" incoming is read after (\d+) with a (\d+) second timeout")]
    public async Task TimedRead(string neuron, long after, int seconds)
        => _timed = (await Ops.ReadAsync(new(neuron, "incoming", after, seconds))).Incoming;

    [When(@"""(.*)"" incoming is read after (\d+) with a (\d+) second timeout while ""(.*)"" fires ""(\w+)"" (\{.*\}) at ""(.*)"" after (\d+) ms")]
    public async Task TimedReadWhileFiring(string neuron, long after, int seconds, string from, string type, string body, string to, int delayMs)
    {
        var firing = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            await Ops.FireAsync(from, new(type, body, to));
        });
        var watch = Stopwatch.StartNew();
        _timed = (await Ops.ReadAsync(new(neuron, "incoming", after, seconds))).Incoming;
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(seconds), "read did not return early on arrival");
        await firing;
    }

    [Then(@"the operation failed with a message containing ""(.*)""")]
    public void ThenFailed(string fragment)
    {
        Assert.NotNull(_error);
        Assert.Contains(fragment, _error.Message, StringComparison.Ordinal);
    }

    [Then(@"the fire result reports (\d+) delivered")]
    public void ThenDelivered(int count)
    {
        Assert.Null(_error);
        Assert.Equal(count, _fire!.Delivered);
    }

    [Then(@"reading ""(.*)"" shows a synapse to ""(.*)"" for ""(\w+)""")]
    public async Task ThenReadSynapse(string neuron, string to, string type)
        => Assert.Contains((await Ops.ReadAsync(new(neuron, "synapses"))).Synapses!, s => s.To == to && s.Type == type);

    [Then(@"reading ""(.*)"" shows latest ""(\w+)"" (\{.*\})$")]
    public async Task ThenReadLatest(string neuron, string type, string body)
        => Assert.Equal(body, (await Ops.ReadAsync(new(neuron, "state"))).State!.Single(s => s.Type == type).Body);

    [Then("the read has state, synapses, incoming and outgoing")]
    public void ThenAllViews()
    {
        Assert.NotNull(_read!.State);
        Assert.NotNull(_read.Synapses);
        Assert.NotNull(_read.Incoming);
        Assert.NotNull(_read.Outgoing);
    }

    [Then("the read has only synapses")]
    public void ThenOnlySynapses()
    {
        Assert.NotNull(_read!.Synapses);
        Assert.Null(_read.State);
        Assert.Null(_read.Incoming);
        Assert.Null(_read.Outgoing);
    }

    [Then(@"the walk visited ""(.*)""")]
    public void ThenWalk(string expected) => Assert.Equal(expected, string.Join(", ", _walk));

    [Then(@"the timed read returned (\d+) entries")]
    public void ThenTimed(int count) => Assert.Equal(count, _timed!.Entries.Count);

    private async Task Try(Func<Task<FireResult>> action)
    {
        _error = null;
        try { _fire = await action(); }
        catch (Exception error) { _error = error; brain.RecordError(error); }
    }
}
```

- [ ] **Step 5: Run to confirm the new features fail to compile / fail**

Run: `dotnet test tests/DigitalBrain.Tests`
Expected: build error `BrainOperations` not found.

- [ ] **Step 6: Write `BrainOperations.cs`**

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Mcp;

// The client. Four operations; the MCP tools are thin wrappers over these.
public sealed class BrainOperations(IGrainFactory grains)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public async Task<FireResult> FireAsync(string session, FireRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var from = Parse(session, nameof(session));
        var signal = Signal.Create(request.Type, request.Body);
        NeuronId? to = request.To is null ? null : Parse(request.To, nameof(request.To));
        CorrelationId? correlation = request.Correlation is null ? null : new CorrelationId(Guid.Parse(request.Correlation));

        var delivered = await Neuron(from).Fire(signal, to, correlation, cancellationToken).ConfigureAwait(false);
        var outgoing = await Query(from).ReadJournal(JournalKind.Outgoing, 0).ConfigureAwait(false);
        var envelope = outgoing.Delta[^1];
        return new(envelope.SignalId.ToString(), envelope.CorrelationId.ToString(), delivered);
    }

    public Task ConnectAsync(ConnectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Signal.Create(request.Type, "{}");
        return Neuron(Parse(request.From, nameof(request.From))).Connect(Parse(request.To, nameof(request.To)), request.Type);
    }

    public Task DisconnectAsync(ConnectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Neuron(Parse(request.From, nameof(request.From))).Disconnect(Parse(request.To, nameof(request.To)), request.Type);
    }

    public async Task<ReadResult> ReadAsync(ReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Parse(request.Neuron, nameof(request.Neuron));
        var query = Query(id);
        var what = request.What?.Trim().ToLowerInvariant();
        if (what is not (null or "" or "state" or "synapses" or "incoming" or "outgoing"))
        {
            throw new ArgumentException($"'{request.What}' is not a view. Use state, synapses, incoming or outgoing, or omit it for all four.", nameof(request));
        }

        var all = string.IsNullOrEmpty(what);
        IReadOnlyList<StateEntry>? state = null;
        IReadOnlyList<SynapseEntry>? synapses = null;
        JournalView? incoming = null;
        JournalView? outgoing = null;

        if (all || what == "state")
        {
            state = [.. (await query.ReadState().ConfigureAwait(false)).Select(d => new StateEntry(d.Signal.Type, d.Signal.Body, d.Source.ToString(), d.Timestamp))];
        }
        if (all || what == "synapses")
        {
            synapses = [.. (await query.ReadSynapses().ConfigureAwait(false)).Select(s => new SynapseEntry(s.Source.ToString(), s.Target.ToString(), s.SignalType))];
        }
        if (all || what == "incoming")
        {
            incoming = await ReadJournalAsync(query, JournalKind.Incoming, request.After, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        }
        if (all || what == "outgoing")
        {
            outgoing = await ReadJournalAsync(query, JournalKind.Outgoing, request.After, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        }

        return new(id.ToString(), state, synapses, incoming, outgoing);
    }

    private static async Task<JournalView> ReadJournalAsync(INeuronQuery query, JournalKind kind, long after, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, timeoutSeconds));
        while (true)
        {
            var read = await query.ReadJournal(kind, after).ConfigureAwait(false);
            if (read.Delta.Count > 0 || timeoutSeconds <= 0 || DateTimeOffset.UtcNow >= deadline)
            {
                var total = read.ResetSnapshot?.TotalRecorded ?? (await query.ReadJournal(kind, read.ResumeSequence + 1).ConfigureAwait(false)).ResetSnapshot?.TotalRecorded ?? read.Delta.Count;
                return new(read.ResumeSequence, [.. read.Delta.Select(d => new JournalEntryView(d.Sequence, d.Signal.Type, d.Signal.Body, d.Source.ToString(), d.SignalId.ToString(), d.CorrelationId.ToString(), d.Timestamp))], total);
            }
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private INeuron Neuron(NeuronId id) => grains.GetGrain<INeuron>(id.ToGrainId());
    private INeuronQuery Query(NeuronId id) => grains.GetGrain<INeuronQuery>(id.ToGrainId());

    private static NeuronId Parse(string text, string parameter)
        => NeuronId.TryParse(text, out var id)
            ? id
            : throw new ArgumentException($"'{text}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", parameter);
}
```

Note on `JournalEntryView.Sequence`: `SignalDelivery.Sequence` is the *source's* outgoing sequence, not the position in this window. Compute the window position instead: `read.ResumeSequence - read.Delta.Count + index + 1`. Use `Select((d, index) => ...)` accordingly.

- [ ] **Step 7: Run and make green**

Run: `dotnet test tests/DigitalBrain.Tests`
Expected: all membrane and read scenarios green. Likely fixes:
- The "rejected with advice" messages come straight from `Signal.Create`, so `_error.Message` matches without unwrapping.
- The timed read that should return early depends on `Deliver` completing within the poll; 100 ms polling and a 300 ms delayed fire satisfy it.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: DigitalBrain.Mcp operations; membrane and read features"
```

---

### Task 5: MCP tools, hosting, and an in-process MCP round trip

**Files:**
- Create: `src/Kernel/DigitalBrain.Mcp/SessionPrincipal.cs`, `BrainTools.cs`, `DigitalBrainMcpHosting.cs`
- Create: `tests/DigitalBrain.Tests/Features/mcp.feature`, `Features/McpSteps.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` (add `ModelContextProtocol.Core` reference for the client)

**Interfaces produced:**

```csharp
namespace DigitalBrain.Mcp;
public sealed class SessionPrincipal { public string Name { get; } }  // default "claude"
public static class DigitalBrainMcpHosting {
    public static IServiceCollection AddDigitalBrainMcp(this IServiceCollection services);
    public static IEndpointRouteBuilder MapDigitalBrainMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp");
}
```

- [ ] **Step 1: Write `mcp.feature`** (drives the real tool layer over an in-memory transport)

```gherkin
Feature: MCP surface
  Four tools over the same operations. An unfamiliar model must be able to use them from
  the descriptions alone.

  Scenario: The server exposes exactly fire, connect, disconnect and read
    Given a running brain
    And an MCP client for principal "claude"
    Then the tools are "connect, disconnect, fire, read"
    And every tool has a description longer than 40 characters

  Scenario: Store, group, recall through the tools
    Given a running brain
    And an MCP client for principal "claude"
    When the tool "fire" is called with {"type":"Note","body":"{\"text\":\"run tests before commit\"}","to":"run-tests"}
    And the tool "connect" is called with {"from":"git","to":"run-tests","type":"Note"}
    And the tool "read" is called with {"neuron":"git","what":"synapses"}
    Then the last tool result contains "run-tests"
    When the tool "read" is called with {"neuron":"run-tests","what":"state"}
    Then the last tool result contains "run tests before commit"

  Scenario: A rejected fire is a tool error with advice
    Given a running brain
    And an MCP client for principal "claude"
    When the tool "fire" is called with {"type":"note 1","body":"{}","to":"x"}
    Then the last tool call failed with a message containing "letters only"
```

- [ ] **Step 2: Write `McpSteps.cs`**

```csharp
using System.IO.Pipelines;
using System.Text.Json;
using DigitalBrain.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class McpSteps(BrainSteps brain)
{
    private McpClient? _client;
    private McpServer? _server;
    private Task? _serverRun;
    private ServiceProvider? _services;
    private CallToolResult? _last;

    [Given(@"an MCP client for principal ""(.*)""")]
    public async Task GivenClient(string principal)
    {
        Pipe toServer = new(), toClient = new();
        var services = new ServiceCollection();
        services.AddSingleton(brain.Brain.Grains);
        services.AddSingleton(new SessionPrincipal(principal));
        services.AddDigitalBrainMcp()
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        _services = services.BuildServiceProvider();
        _server = _services.GetRequiredService<McpServer>();
        _serverRun = _server.RunAsync();
        _client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
    }

    [When(@"the tool ""(\w+)"" is called with (\{.*\})$")]
    public async Task CallTool(string name, string json)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        _last = await _client!.CallToolAsync(name, args);
    }

    [Then(@"the tools are ""(.*)""")]
    public async Task ThenTools(string expected)
        => Assert.Equal(expected, string.Join(", ", (await _client!.ListToolsAsync()).Select(t => t.Name).Order(StringComparer.Ordinal)));

    [Then(@"every tool has a description longer than (\d+) characters")]
    public async Task ThenDescribed(int length)
        => Assert.All(await _client!.ListToolsAsync(), t => Assert.True((t.Description?.Length ?? 0) > length, t.Name));

    [Then(@"the last tool result contains ""(.*)""")]
    public void ThenResultContains(string fragment)
    {
        Assert.False(_last!.IsError ?? false, Text(_last));
        Assert.Contains(fragment, Text(_last), StringComparison.Ordinal);
    }

    [Then(@"the last tool call failed with a message containing ""(.*)""")]
    public void ThenFailed(string fragment)
    {
        Assert.True(_last!.IsError ?? false);
        Assert.Contains(fragment, Text(_last), StringComparison.Ordinal);
    }

    [AfterScenario]
    public async Task Teardown()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_server is not null) await _server.DisposeAsync();
        if (_serverRun is not null) { try { await _serverRun; } catch (OperationCanceledException) { } }
        if (_services is not null) await _services.DisposeAsync();
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
```

Add `<PackageReference Include="ModelContextProtocol.Core" />` to the tests csproj (the version is already pinned centrally).

- [ ] **Step 3: Run to see the failure**

Run: `dotnet test tests/DigitalBrain.Tests --filter "FullyQualifiedName~Mcp"`
Expected: build error `AddDigitalBrainMcp` / `SessionPrincipal` not found.

- [ ] **Step 4: Write `SessionPrincipal.cs`**

```csharp
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Mcp;

// The caller's Session neuron name. Over HTTP it comes from ?principal= or the
// X-DigitalBrain-Principal header; otherwise "claude". One name, one Session, across connections.
public sealed class SessionPrincipal
{
    public const string Default = "claude";
    public const string QueryKey = "principal";
    public const string HeaderName = "X-DigitalBrain-Principal";

    public SessionPrincipal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public string Name { get; }

    public static SessionPrincipal FromHttp(HttpContext? context)
    {
        var name = context?.Request.Query[QueryKey].FirstOrDefault()
            ?? context?.Request.Headers[HeaderName].FirstOrDefault();
        return new(string.IsNullOrWhiteSpace(name) ? Default : name);
    }
}
```

- [ ] **Step 5: Write `BrainTools.cs`**

```csharp
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
public sealed class BrainTools(BrainOperations operations, SessionPrincipal session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "fire"), Description(
        "Fire a signal from your Session neuron. A signal is a `type` (letters only, vocabulary such as Note, Confirmed, Decision) "
        + "and a JSON `body` up to 64 KB. With `to`, it goes to exactly that neuron and creates the synapse if missing; "
        + "without `to`, it follows every synapse of that type you already have. Neurons exist as soon as they are named. "
        + "Put identity in the neuron name (run-tests-before-commit), never in the type. Returns the signal id, correlation and how many neurons received it.")]
    public async Task<string> Fire(
        [Description("Signal type: letters only, e.g. Note")] string type,
        [Description("JSON body, e.g. {\"text\":\"run tests before commit\"}. Empty means {}.")] string body,
        [Description("Target neuron name, e.g. run-tests. Omit to follow all your synapses of this type.")] string? to = null,
        [Description("Optional correlation id (GUID) to tie this to an earlier signal.")] string? correlation = null,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await operations.FireAsync(session.Name, new(type, body, to, correlation), cancellationToken).ConfigureAwait(false), Json);

    [McpServerTool(Name = "connect"), Description(
        "Create a synapse: from one neuron to another for one signal type. Idempotent. Use it to build structure, "
        + "e.g. connect topic `git` to `run-tests-before-commit` for `Note`, then recall by reading `git`'s synapses and following them.")]
    public async Task<string> Connect(
        [Description("Source neuron name")] string from,
        [Description("Target neuron name")] string to,
        [Description("Signal type the synapse carries, letters only")] string type,
        CancellationToken cancellationToken = default)
    {
        await operations.ConnectAsync(new(from, to, type), cancellationToken).ConfigureAwait(false);
        return $"connected {from} --{type}--> {to}";
    }

    [McpServerTool(Name = "disconnect"), Description("Remove a synapse. Succeeds even if it did not exist.")]
    public async Task<string> Disconnect(
        [Description("Source neuron name")] string from,
        [Description("Target neuron name")] string to,
        [Description("Signal type")] string type,
        CancellationToken cancellationToken = default)
    {
        await operations.DisconnectAsync(new(from, to, type), cancellationToken).ConfigureAwait(false);
        return $"disconnected {from} --{type}--> {to}";
    }

    [McpServerTool(Name = "read"), Description(
        "Read a neuron without changing anything. Returns its state (latest signal per type), its synapses, and its incoming and outgoing journals. "
        + "Recall pattern: read a topic's synapses, follow each target, read its state. "
        + "`what` narrows to state | synapses | incoming | outgoing. `after` is a journal sequence to resume from. "
        + "`timeoutSeconds` makes an incoming/outgoing read wait for the next entry. Your own Session neuron is named after your principal (default `claude`).")]
    public async Task<string> Read(
        [Description("Neuron name, e.g. git or run-tests-before-commit")] string neuron,
        [Description("state | synapses | incoming | outgoing; omit for all four")] string? what = null,
        [Description("Journal sequence to read after; 0 for the retained window")] long after = 0,
        [Description("Seconds to wait for a new journal entry; 0 returns immediately")] int timeoutSeconds = 0,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await operations.ReadAsync(new(neuron, what, after, timeoutSeconds), cancellationToken).ConfigureAwait(false), Json);
}
```

- [ ] **Step 6: Write `DigitalBrainMcpHosting.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

public static class DigitalBrainMcpHosting
{
    // Registers the operations, tools and MCP server. The caller adds a transport:
    // WithHttpTransport for the Silo, WithStreamServerTransport for tests.
    public static IMcpServerBuilder AddDigitalBrainMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<BrainOperations>();
        services.AddHttpContextAccessor();
        services.TryAddScoped(sp => SessionPrincipal.FromHttp(sp.GetRequiredService<IHttpContextAccessor>().HttpContext));
        return services.AddMcpServer(options => options.ServerInfo = new() { Name = "digitalbrain", Version = "0.1" })
            .WithTools<BrainTools>();
    }

    public static IEndpointRouteBuilder MapDigitalBrainMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapMcp(pattern);
        return endpoints;
    }
}
```

In tests, `services.AddSingleton(new SessionPrincipal(principal))` is registered before `AddDigitalBrainMcp`, so `TryAddScoped` does not override it. Tool errors: the SDK turns a thrown exception in a tool into a `CallToolResult` with `IsError = true` and the exception message as text. If the message is replaced by a generic one, catch `Exception` in each tool method and throw `McpException(error.Message)` instead.

- [ ] **Step 7: Run and make green**

Run: `dotnet test tests/DigitalBrain.Tests`
Expected: whole suite green including the three MCP scenarios.

- [ ] **Step 8: Wire the Silo**

`src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj`: add `<ProjectReference Include="../DigitalBrain.Mcp/DigitalBrain.Mcp.csproj" />`.

`Program.cs`:
```csharp
using DigitalBrain.Aspire;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using ModelContextProtocol.AspNetCore;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.AddKernelCors();
builder.Services.AddDigitalBrainMcp()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);

var app = builder.Build();

app.UseKernelCors();
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.MapDigitalBrainMcp("/mcp");

app.Run();
```

Run: `dotnet build DigitalBrain.slnx` clean.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: MCP tools fire/connect/disconnect/read hosted at /mcp"
```

---

### Task 6: Run under Aspire and benchmark with Grok CLI

**Files:**
- Create: `docs/architecture/grok-benchmark.md` (script + scorecard)

- [ ] **Step 1: Start the AppHost**

Run: `aspire start --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj --non-interactive`
Use the Aspire MCP `list_resources` to get the kernel's HTTP endpoint. Confirm `GET <kernel>/health` is 200 and `POST <kernel>/mcp` answers an MCP `initialize`.

- [ ] **Step 2: Point Grok CLI at the server**

Add to Grok CLI's MCP config a streamable-HTTP server named `digitalbrain` with URL `<kernel>/mcp?principal=grok`. Confirm `tools/list` shows the four tools.

- [ ] **Step 3: Run the benchmark script, verbatim, as three separate prompts**

1. "Remember that I always want tests run before a commit, and file it under a topic called git."
2. (after `aspire stop` / `aspire start`) "What do you know about how I want to work with git?"
3. "Correction: only run the affected tests, not the whole suite. Update what you remembered."

Score each prompt 0-2: 0 = wrong or gave up, 1 = right after a retry or with a wrong tool first, 2 = right first time. Record which tool calls were made, in order, and any error messages the model hit.

- [ ] **Step 4: Verify from Claude Code's side**

Using this repo's `digitalbrain-mcp` server (same URL, default principal `claude`): `read` neuron `git`, follow its synapses, `read` the target's state. The latest `Note` must be the corrected text. Read `grok`'s outgoing journal and confirm it shows the three fires.

- [ ] **Step 5: Tune and re-run**

If any prompt scored below 2, change only tool descriptions or error messages in `BrainTools.cs` / `Signal.cs` / `BrainOperations.cs`, rerun the suite, restart, rerun that prompt. Stop when all three score 2 or after three rounds; record the final scorecard and the description changes in `docs/architecture/grok-benchmark.md`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "docs: Grok CLI benchmark of the MCP surface, description tuning"
```

---

## Self-review

**Spec coverage**
- 01 invariants 1-7: Task 2 (1, 2, 3, 5), Task 3 (4 via profile ignoring unknown types, 6, 7). ✔
- 02 invariants 1-7: Task 3 (1, 2, 3), Task 4 (4, 5, 6), Task 2 connect.feature last scenario (7). ✔
- 03 invariants 1-7: Task 4 read.feature (1, 2, 4, 5, 7), Task 2 connect (3), Task 4 membrane (6). Tool descriptions and error table: Task 5. Benchmark: Task 6. ✔
- 04 project shape: Task 1 (Sdk deleted, Testing kept, tests renamed), Task 4 (Mcp created), Aspire edits limited to step 9. ✔
- Observation: `AddActivityPropagation` added in Task 1 step 8. ✔

**Placeholder scan**: none. Every code step carries its code.

**Type consistency**: `Signal.Create`, `NeuronId.Plain/TryParse/FromGrainId`, `INeuron.Fire(Signal, NeuronId?, CorrelationId?, CancellationToken)`, `INeuronQuery.ReadState/ReadSynapses/ReadJournal`, `BrainOperations.FireAsync(string session, FireRequest, ct)`, `ReadRequest(Neuron, What, After, TimeoutSeconds)`, `SessionPrincipal(string)`, `AddDigitalBrainMcp()` returning `IMcpServerBuilder` are used identically across Tasks 1-5.

**Known judgement calls the executor may hit**
- `IDurableDictionary<string, SignalDelivery>` keyed `"latest"`: Orleans.Journaling resolves keyed durable collections by name for any serializable value; `SignalDelivery` is `[GenerateSerializer]` and JSON-friendly. If the JSON journal format rejects the nested `Signal` record, mark `Signal` with `[method: JsonConstructor]` on its private constructor or make the constructor public.
- If Reqnroll cannot inject `BrainSteps` into other step classes because it is `sealed`, drop `sealed`.
