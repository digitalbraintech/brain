# DigitalBrain / NeuroOS — System Design & Way of Working

Status snapshot as of master `1e96963` (2026-07-01). This is a technical reference, grounded in the
current code and docs — every load-bearing claim below cites a file (and line, where it matters).

---

## Part 1 — System Design

### 1.1 The Core Law

Everything is a **Neuron** or a **Synapse**.

- **Neuron** — an Orleans grain, `INeuron : IGrainWithStringKey` (`DigitalBrain.Core/INeuron.cs:3-21`).
  Members: `FireAsync<T>`, `GetTimelineAsync`, `DeliverAsync`, `GetIncomingTimelineAsync`/
  `GetOutgoingTimelineAsync`, `GetCausalLineageAsync`/`GetTimelineForCorrelationAsync`, plus checkpoint/
  branch/restore (`CreateCheckpointAsync`, `BranchAsync`, `RestoreCheckpointAsync`). The base
  implementation is `DigitalBrain.Kernel/Neuron.cs`.
- **Synapse** — an immutable typed message, `DigitalBrain.Core/Synapse.cs:6-34`:
  ```csharp
  record Synapse(string Type, DateTimeOffset Timestamp, NeuronId? Sender = null,
      NeuronId? Receiver = null, bool IsBroadcast = false, string? CorrelationId = null)
  ```
  plus init-only `SynapseId` (a GUID) and `CausationId`. `Stamp(NeuronId sender, Synapse? cause)`
  (lines 26-33) sets `Sender`/`Timestamp`, propagates `CorrelationId` (explicit > cause's correlation >
  cause's `SynapseId` > own `SynapseId`), and sets `CausationId` to the cause's `SynapseId` — this is
  the causal-lineage chain the checkpoint/branch machinery relies on.
- **`IHandle<T>`** (`DigitalBrain.Core/IHandle.cs:3-6`) — the wiring contract: `interface IHandle<T>
  where T : Synapse { Task HandleAsync(T synapse); }`. A neuron declares one of these per synapse type
  it consumes; the set is scanned reflectively (see 1.2) rather than registered by hand.

### 1.2 Message dispatch: broadcast fan-out is real, not stubbed

**This was the single fact most worth re-verifying, and it checks out: broadcast fan-out over an
Orleans stream is live on master today.** The historical "empty `if (stamped.IsBroadcast) {}` stub"
does not exist in the current tree — it was restored by `f44d061 feat(kernel): restore timeline
broadcast + reflection dispatch (harvest Projects/final)` and `d81b4fa feat(kernel): add
DigitalBrainTimeline stream accessor + provider`, both already in `master`'s history.

`Neuron.FireAsync<T>` (`DigitalBrain.Kernel/Neuron.cs:157-179`):

```csharp
public async ValueTask FireAsync<T>(T payload) where T : Synapse
{
    var stamped = payload.Stamp(Self, _currentCause);
    AddToJournal(ref _outgoingSynapses, "out-journal", stamped);
    await WriteJournalStateAsync();

    if (stamped.IsBroadcast)
        await this.GetStreamProvider(SynapseStream.ProviderName).Timeline().OnNextAsync(stamped);
    else if (stamped.Receiver is not null) { /* point-to-point via GrainFactory */ }
    else { /* self-deliver */ }
}
```

`SynapseStream` (`DigitalBrain.Kernel/SynapseStream.cs:8-11`) defines the provider name
`"DigitalBrainTimeline"` and `StreamId.Create("timeline", "global")` — a single global stream, not
per-topic. `OnActivateAsync` (`Neuron.cs:77-102`) calls `SubscribeTimelineIfNeeded()` (lines
114-140), gated by `ShouldSubscribeToTimeline` (line 107) = `SynapseDispatch.HandledTypes(GetType())
.Count > 0` — a neuron only subscribes to the global broadcast stream if it statically declares at
least one `IHandle<T>`; it resumes existing subscription handles rather than double-subscribing.
Broadcast delivery (`OnNextAsync`, lines 144-147) calls `SynapseDispatch.DispatchAsync`, which caches
`IHandle<T>` methods per grain type and invokes `HandleAsync` reflectively
(`SynapseDispatch.cs:20-27`). Point-to-point delivery uses a separate reflection path,
`TryHandleViaDeclaredInterfaceAsync` (`Neuron.cs:278-301`), reached via `DeliverAsync` (lines
239-271).

The real silo registers the stream provider in production, not just tests:
`Program.cs:164` — `siloBuilder.AddMemoryStreams("DigitalBrainTimeline")`.

Proof of actual N+1 fan-out: `DigitalBrain.Tests/Kernel/BroadcastReactivityTests.cs`, scenario
`Broadcast_reaches_every_activated_handler` — two independent `PingSink` grains both activate and
both independently receive a single broadcast `Ping`, with no direct addressing between them. Also
`DigitalBrain.Tests/Kernel/TimelineStreamTests.cs` asserts the provider name and stream id directly.
The stale docs at `docs/superpowers/specs/2026-06-30-telegram-llm-experience-design.md` describe the
*plan* to restore this — they predate the fix and should not be read as current-state.

### 1.3 Project graph

Solution `Brain.slnx` lists 29 projects plus the Flutter client folder reference (`../app/Flutter.proj`).

| Project | Purpose |
|---|---|
| `DigitalBrain.Core` | Pure protocol layer — `INeuron`, `Synapse`, `IHandle<T>`, `NeuronId`/`TaskId`, `IPackBehavior`, checkpoint/trust/UI-surface contracts, marketplace seeds. Only depends on `Microsoft.Orleans.Core.Abstractions`/`Serialization`. Kept dependency-light so `Mcp.Tools` can reference only this. Zero references to any specific integration/vendor type (Google, Gmail, Winget, Git, Roslyn, Flutter UI, Telegram chat, Context grain, …) — only generic `*Signals` string-constant registries, which are name registries, not type contracts. |
| `DigitalBrain.Kernel` | The Orleans runtime (formerly "Silo"). `Microsoft.NET.Sdk.Web`, container-packaged. Subfolders: `Auth, Awesome, Company, Config, Context, Economics, Foundry, Gateway, Generated, Ino, Kernel, Llm, Marketplace, Protos, Sandbox, Sdk, Ui`. Owns embodiment (Foundry/Sandbox), LLM (Microsoft.Extensions.AI + Ollama/Azure OpenAI), Economics (Stripe + ECDSA licenses), Context (Qdrant), server-driven UI (`Ui`/`Protos`, bidirectional gRPC `UiGateway`), HA self-update. References every isolated ino project below for their interfaces/logic, and hosts the concrete `Neuron`-derived grain classes for Windows/Developer/Context/UiKit/Telegram.Channel/Google (see §1.3a). |
| `DigitalBrain.Aspire` | Hosting SDK: `AddDigitalBrain`, `WireKernelSilo`, `WithMcp`, `WithOrleansDashboard`, `AddFlutterClient`, `WireTelegramTransport` — all in `DigitalBrainBuilderExtensions.cs`. Not itself an Aspire project resource (`IsAspireProjectResource=false`). |
| `DigitalBrain.Gateway` | Thin/legacy HTTP entry point; Orleans client + clustering only. Gated behind `DIGITALBRAIN_ENABLE_DIAGNOSTIC_GATEWAY` in the AppHost — the kernel hosts the product gRPC/surface gateway by default now. |
| `DigitalBrain.Mcp` / `DigitalBrain.Mcp.Tools` | MCP server (`Mcp`, an `Exe`, co-hosted on the kernel's Kestrel) exposing neuron tools defined in `Mcp.Tools` (`DigitalBrainMutationTools.cs`, `DigitalBrainReadTools.cs`) — `Mcp.Tools` references only `Core`. |
| `DigitalBrain.Cli` | Spectre.Console TUI, `start-ui` resource, `.WithExplicitStart()` — not auto-started with the rest of the graph. |
| `DigitalBrain.Telegram` | Pure shared library: transport-internal synapses (`TelegramMessageReceived`, `TelegramReplyRequested` — explicitly *not* journaled through the kernel) and `TelegramResponderNeuron`, a pure `IPackBehavior` pack. Fully self-contained — never derives from `Neuron`. |
| `DigitalBrain.Telegram.Transport` | The actual ASP.NET Core webhook host — `/webhook` POST ingress, `/health`, gRPC-clients the kernel gateway. Deployed as its own container app (see 1.6). |
| `DigitalBrain.TestKit` | `IDigitalBrain` facade over a real Orleans `TestCluster`, depends on Core + Kernel + every real-grain ino below. Gives each isolated ino's `.Tests` sibling a zero-boilerplate way to spin a cluster and resolve grains without depending on `DigitalBrain.Tests`. |
| `DigitalBrain.Windows` (+ `.Tests`) | Real-grain ino. `IFileSystemNeuron`/`IWingetNeuron`/`IShellNeuron` interfaces, `ProcessRunner`, `FileSystemOperations` (real `System.IO` calls) — Core-only dependency. Grain classes stay in Kernel (§1.3a). |
| `DigitalBrain.Developer` (+ `.Tests`) | Real-grain ino. `IGitNeuron`/`IDotNetNeuron`/`INuGetNeuron`/`IRoslynNeuron` interfaces plus real, Orleans-independent logic (e.g. `RoslynAnalysisService` via `MSBuildWorkspace`) — Core-only. Grain classes stay in Kernel. |
| `DigitalBrain.Context` (+ `.Tests`) | Real-grain ino. `IContextNeuron` interface plus the Qdrant memory subsystem (`ContextServices`, `DocumentIngestor`, `HybridScorer`, `QdrantVectorStore`, `VectorStore`) — Core-only. `ContextNeuron` grain class stays in Kernel. |
| `DigitalBrain.UiKit` (+ `.Tests`) | Real-grain ino, interface-only (see §1.3a honest limitation). `IFlutterUiNeuron` interface — Core-only. `FlutterUiNeuron` grain class, `HomeFeedBus`, `SignalEgressBus`, `UiSurfaceRfwBridge` all stay in Kernel as cross-cutting broadcast infra. |
| `DigitalBrain.Telegram.Channel` (+ `.Tests`) | Real-grain ino, interface-only (see §1.3a honest limitation). `ITelegramChatNeuron` interface — Core-only. `TelegramChatNeuron` grain class (stateful, journal-derived binding/routing) stays in Kernel. |
| `DigitalBrain.Google` (+ `.Tests`) | Real-grain ino. `IGmailNeuron`/`IGoogleDriveNeuron`/`IGoogleCalendarNeuron` interfaces, `I*ApiClient` wrapper interfaces + real `Google*ApiClient` implementations, `GoogleCredentialFactory` — Core + `Google.Apis.*` only. Grain classes (`GmailNeuron`, `GoogleDriveNeuron`, `GoogleCalendarNeuron`, `GoogleAuthNeuron`) live in Kernel, not this project (the one correction the neuron-placement amendment made to the base spec). |
| `DigitalBrain.Experience.PersonalAssistant` (+ `.Tests`) | Pure-pack ino. `PersonalAssistantNeuron : IPackBehavior` — multi-hop: recall Context → augmented `AskLlm` → text reply or visualize, composing Telegram+Context+LLM via generic `Signal` names. Fully self-contained — never derives from `Neuron`. Core-only. Its source is embedded into `DigitalBrain.Core` and seeded into the marketplace at runtime (`MarketplaceSeeds.PersonalAssistantPackCode`). |
| `NeuroOSPrototype.AppHost` + `NeuroOSPrototype.ServiceDefaults` | The Aspire host resource graph (below) and standard OTel/health/resilience defaults. |
| `DigitalBrain.Tests` | Reqnroll BDD + xUnit over a real Orleans `TestCluster`. See Part 2. |

### 1.3a Isolated inos: every integration's contract is a peer, not a Kernel internal

As of the 2026-07-01 isolated-ino migration (design: `docs/superpowers/specs/2026-07-01-isolated-testable-marketplace-inos-design.md`;
amended by `docs/superpowers/specs/2026-07-01-real-grain-ino-neuron-placement-amendment.md`), every
marketplace integration that used to live entirely inside `DigitalBrain.Kernel` now has its public
interface (and, where genuinely Orleans-independent, its real capability logic) extracted into its own
Core-only peer project — `DigitalBrain.Windows`, `.Developer`, `.Context`, `.UiKit`, `.Telegram.Channel`,
`.Google`, plus the pure-pack `.Telegram` and `.Experience.PersonalAssistant`. `DigitalBrain.Kernel`
references each of these (the dependency direction is unchanged: Kernel depends on the inos, not the
reverse), and `DigitalBrain.Core` has zero awareness of any of them — verified by grepping `DigitalBrain.Core`
for every moved type name.

The split is **not** "each ino hosts its own grain." `Neuron`/`NeuronJournals` are built on Orleans's alpha
`Journaling`/`DurableGrain` APIs and live in `DigitalBrain.Kernel` only — they always have, and this
migration does not move them. So the concrete `Neuron`-derived grain classes for Windows, Developer,
Context, UiKit, Telegram.Channel, and Google all stay in (or, for Google, moved into)
`DigitalBrain.Kernel`, which constructor-injects and delegates to the ino's extracted logic where one
exists. Only two ino families are fully self-contained `IPackBehavior` packs with no Kernel-side grain at
all: `DigitalBrain.Telegram` (`TelegramResponderNeuron`) and `DigitalBrain.Experience.PersonalAssistant`
(`PersonalAssistantNeuron`) — neither ever derived from `Neuron`, so neither was blocked by the
Orleans-dependency issue the amendment addresses.

How much real logic actually moved out varies by ino, per the amendment's own "honest limitation":
- **Windows / Developer / Context / Google** — a genuine win: real capability logic (file I/O, git/dotnet/
  nuget process wrapping, Qdrant vector-store calls, Gmail/Drive/Calendar HTTP calls) is fully isolated and
  independently unit-testable with zero Orleans infrastructure.
- **UiKit / Telegram.Channel** — a smaller win: these grains' bodies are inherently Kernel-coupled
  (`HomeFeedBus`, `SignalEgressBus`, `UiSurfaceRfwBridge`, journal-derived routing state all stay in
  Kernel as cross-cutting broadcast infra), so their ino projects hold only the public interface. Still
  worthwhile — it gives other consumers a small, generic-free dependency surface — but not the same
  "capability logic fully out of Kernel" outcome as the first group.

`DigitalBrain.TestKit` (Task 1) is the shared foundation all of this rests on: an `IDigitalBrain` facade
over a real Orleans `TestCluster`, depended on by Kernel and by every real-grain ino's `.Tests` sibling, so
each isolated project gets its own fast, direct grain-resolution tests without routing through the central
`DigitalBrain.Tests` project (which remains the broadest cross-cutting E2E safety net).

Target framework: **net11.0** (`DigitalBrain.Core.csproj:4`). Key pinned versions
(`Directory.Packages.props`, captured 2026-06-21 per its own header comment): Aspire.* **13.4.6**
(`CommunityToolkit.Aspire.Hosting.Ollama` **13.4.0**); Orleans Client/Server/Clustering.AzureStorage/
Persistence.AzureStorage **10.2.0**, but Core/Core.Abstractions/Serialization/Server/Streaming/
Persistence.Memory **10.2.1-preview.1** and `Microsoft.Orleans.Journaling`
**10.2.1-preview.1.alpha.1**; Reqnroll/Reqnroll.xUnit **3.3.4**; Microsoft.Extensions.AI(.OpenAI)
**10.7.0**; Microsoft.Playwright **1.49.0**; Grpc.AspNetCore/Grpc.Net.Client/Grpc.Tools **2.71.0**,
Google.Protobuf **3.31.0**; Stripe.net **50.0.0**; Qdrant.Client **1.18.1**;
Azure.Extensions.AspNetCore.DataProtection.Blobs **1.5.3**. No `Version="*"` anywhere — updates are
deliberate.

### 1.4 AppHost resource graph (current, `NeuroOSPrototype.AppHost/AppHost.cs`)

```csharp
builder.AddDigitalBrain("digitalbrain", options => {
    LlmModel = "qwen2.5-coder:1.5b"; UseLocalMarketplace = true;
}).WithOrleansDashboard(8080).WithMcp();                              // AppHost.cs:14-20
```

`AddDigitalBrain` (`DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:35-83`) provisions an Azure
Storage emulator (`storage`) with a `clustering` table and `grainstate`/`journal` blob containers, an
Orleans clustering service (`kernel`) backed by those, and an Ollama container (GPU-enabled, data
volume) running the configured model. `WireKernelSilo` (lines 104-132) wires the kernel project to
Orleans/clustering/grain-state/journal/LLM, adds `grpc` + `web` endpoints,
`WithExternalHttpEndpoints()`, and **`WithReplicas(ctx.KernelReplicas)`** — default **3 replicas**
(`DigitalBrainOptions.KernelReplicas`, line 210), overridable via `DIGITALBRAIN_KERNEL_REPLICAS`.
`start-ui` (`DigitalBrain.Cli`) is added with `.WithExplicitStart()`. Flutter client and MCP/Telegram
are conditional:

- **Flutter**: only added if `app/pubspec.yaml` resolves on disk (`ResolveFlutterAppPath`,
  AppHost.cs:41-87) — `ctx.AddFlutterClient("flutter-ui", flutterUiPath, "windows")`, which runs
  `flutter run -d windows` as an executable resource.
- **MCP**: gated by `ctx.EnableMcp` (default true) — adds `DigitalBrain.Mcp` referencing the Orleans
  client + LLM.
- **Telegram**: gated by env `DIGITALBRAIN_ENABLE_TELEGRAM` — adds `DigitalBrain.Telegram.Transport`
  as `telegram-bot`, wired via `ctx.WireTelegramTransport(...)`.
- **Legacy diagnostic gateway**: gated by `DIGITALBRAIN_ENABLE_DIAGNOSTIC_GATEWAY`, off by default.

All six extension methods (`AddDigitalBrain`, `WireKernelSilo`, `WithMcp`, `WithOrleansDashboard`,
`AddFlutterClient`, `WireTelegramTransport`) live in `DigitalBrainBuilderExtensions.cs` and are real,
not aspirational.

### 1.5 NeuroPack lifecycle: author → sign → publish → trust-gate → compile → embody → dispatch

`IPackBehavior` (`DigitalBrain.Core/Distribution/IPackBehavior.cs:23-43`) is the pure/sync contract a
pack implements — no `IChatClient`, no DI, "a pack assembly references only this Protocol assembly"
(comment, lines 14-17). Only `string Respond(string input)` is required; `GetManifest()`,
`GetBundleManifest()`, `CanHandle(Synapse)`, `Handle(Synapse)` have default implementations that route
`ExperienceUsed` through `Respond` and wrap the result as a `PackEmission`.

`NeuroPack` (`DigitalBrain.Core/Synapse.cs:236-251`) is the wire format: `Name, Version, OwnerId,
IsPrivate, CommissionRate, Code, Description, AuthorPublicKeyBase64, SignatureBase64, Price, Manifest`
— `Manifest` (a `BundleManifest?`) is carried directly on the pack record, populated lazily at publish
time (see below), not a parallel type.

**Signing** — `DigitalBrain.Core/Trust/PackSignatureVerifier.cs`. ECDSA-nistP256
(`ECDsa.Create(ECCurve.NamedCurves.nistP256)`). Canonical content =
`Name|Version|SHA256(Code)|AuthorPublicKeyBase64`; `VerifyPack` recomputes it and calls
`ecdsa.VerifyData(..., HashAlgorithmName.SHA256)`, treating `CryptographicException`/
`FormatException`/`ArgumentException` as a plain `false` rather than a crash.

**Trust gate** — `DigitalBrain.Core/Trust/PublisherTrust.cs:9-13`: `IsTrusted` = signature verifies
**AND** `trustedPublisherKeys.Contains(pack.AuthorPublicKeyBase64)` — integrity and trust are
deliberately separate checks (a validly self-signed stranger still fails the allowlist). Enforced in
`MarketplaceNeuron.HandleAsync(PublishToMarketplace)` (`DigitalBrain.Kernel/SystemNeurons.cs:499`):
publishing is rejected outright when `DigitalBrain:Marketplace:GatePublishing` is `true` (config
default `false`) and the publisher isn't trusted; the gate is re-applied on cache rebuild from journal
replay so a rejected pack stays excluded across restarts. Install has its own separate gate
(`InstallFromMarketplace`, lines 563-611): invalid signatures always rejected; unsigned packs rejected
by default (`RejectUnsignedPacks`, default `true`); priced packs additionally require a license
entitlement.

**Compile → ALC → embody** — `DigitalBrain.Kernel/Foundry/`:
- `FoundryCompilation.CreateWith` (`FoundryCompilation.cs:20-25`) parses source with
  `CSharpSyntaxTree.ParseText` and compiles against the trusted-platform-assemblies plus
  `DigitalBrain.Core`, targeting `OutputKind.DynamicallyLinkedLibrary`.
- `PackAlcEmbodier.Embody` (`PackAlcEmbodier.cs:42-80`) compiles, then runs
  `CapabilityGate.FindViolations(compilation)` — a semantic-model walk banning
  `System.Diagnostics.Process`, `System.Reflection.Emit`, `System.Runtime.InteropServices`,
  `System.Runtime.Loader`, `Microsoft.Win32.Registry` — and throws `PackEmbodimentException` on a hit.
  It emits to a `MemoryStream`, creates a **collectible** `AssemblyLoadContext` under
  `ExecutionContext.SuppressFlow()`, wires `Resolving += ResolveFromHost` so shared types like
  `DigitalBrain.Core` unify with the host assembly (making the `IPackBehavior` cast valid across the
  ALC boundary), loads the pack assembly, and instantiates the first public parameterless
  `IPackBehavior` type it finds. Returns an `EmbodiedPack` whose `Dispose()` calls
  `context.Unload()` — this is the mechanism that lets packs be added/removed without a silo restart.
- Embody happens at two points: `MarketplaceNeuron.MaterializeManifest` (best-effort, at publish time,
  purely to extract `BundleManifest` for catalog faceting) and `GeneratedNeuron.TryEmbody`
  (`SystemNeurons.cs:975-998`, at install time — this is the one that becomes the live dispatch
  target).
- **Dispatch**: `GeneratedNeuron` forces `ShouldSubscribeToTimeline => true` (line 918) regardless of
  static `IHandle<T>` declarations, because its handled types are only known dynamically — its
  `OnNextAsync` (924-931) tries `_embodied.CanHandle`/`Handle` first, then falls back to the base
  static-`IHandle<T>` path. This is the concrete "N+1" mechanism: installing a new pack adds a new
  `GeneratedNeuron` subscriber to the same global broadcast stream, so an existing broadcast synapse
  now reaches one more handler with zero silo restart.

### 1.6 Bundle / Manifest layer

`BundleManifest` (`DigitalBrain.Core/Distribution/BundleManifest.cs:23-27`):

```csharp
public record BundleManifest(
    BundleTier Tier,                                 // Substrate | Channel | Content
    ExperienceRef? EntryExperience,                  // (ExperienceId, EntryEvent = "start")
    IReadOnlyList<BundleChannel> Channels,           // InApp | Telegram | Web
    IReadOnlyList<BundleDependency>? Dependencies);  // (PackName, MinVersion)
```

This is deliberately *not* a new runtime type layered as a separate grain/install/trust/version path —
per `docs/specs/2026-07-01-distribution-and-bundles.md` §5.2, that was explicitly rejected ("Dumb as
stated... deleted in favor of `NeuroPack` + a manifest"). `GetBundleManifest()` is a default member on
`IPackBehavior`; a pack's own compiled code produces it, and `MarketplaceNeuron.MaterializeManifest`
folds it onto `NeuroPack.Manifest` at publish time (`pack with { Manifest = manifest }`). Three tiers
in practice: **Substrate** (`kernel`, `ui-kit` — the platform itself, shipped as packs), **Channel**
(`telegram`, `web` — delivery surfaces), **Content** (the actual creator-authored micro-apps).

Faceted discovery: `FilterMarketplace` synapse (`Synapse.cs:37-40`, fields `Tier?`, `Channel?`),
handled by `MarketplaceNeuron.HandleAsync(FilterMarketplace)` (`SystemNeurons.cs:672-682`), which
projects through `UiSurfaceLiveData.MarketplaceTreeSurface` (`Core/UiSurfaces.cs:836+`) and broadcasts
the filtered result to `HomeFeedBus`.

`BundleHarness` (`DigitalBrain.Tests/Ui/BundleHarness.cs`) is the fast-authoring test harness: it
constructs `new PackAlcEmbodier().Embody(pack, packCode)` directly — the identical Roslyn/ALC path
production uses — with no Aspire host and no browser, then exposes `Trigger(eventName, args)` (fires
an `ExperienceStep` synapse through `_pack.Handle`) and `GetTree` (extracts the resulting
`UiWidgetTree`). This is what makes "author → green test with live-rendered UI" a single
`dotnet test` run.

### 1.7 Telegram integration architecture

`TelegramChatNeuron` (`DigitalBrain.Kernel/TelegramChatNeuron.cs`, 91 lines) binds a chat to a bundle
**implicitly** — there's no separate bind-state write. `BoundBundle()` (lines 63-75) scans the
neuron's own `IncomingJournal` backward for the most recent `Signal("TelegramMessageReceived")` whose
text matches `/start <id>`; the journal entry *is* the binding. Once bound, ordinary messages route
point-to-point:

```csharp
var receiver = new NeuronId("generated-" + bound.ToLowerInvariant());        // line 48
var stamped = (signal with { Receiver = receiver }).Stamp(Self, CurrentCause);
await GrainFactory.GetGrain<IGeneratedNeuron>(receiver.Value).DeliverAsync(stamped);
```

Unbound chats fall back to `Broadcast(signal)` (line 58), reaching whatever global responder is
subscribed. `ShouldSubscribeToTimeline => false` (line 19) is deliberate — it stops the neuron from
re-triggering itself on its own outbound broadcasts.

Two projects split transport from logic:
- **`DigitalBrain.Telegram`** — pure shared library. `TelegramResponderNeuron` is a plain
  `IPackBehavior` pack (no System.Net/Process/Reflection.Emit) that turns
  `Signal("TelegramMessageReceived")` into an `AskLlm`. `Synapses.cs` defines transport-internal
  records (`TelegramMessageReceived`, `TelegramReplyRequested`) explicitly *not* journaled through the
  kernel.
- **`DigitalBrain.Telegram.Transport`** — the real ASP.NET Core webhook host (`Program.cs`):
  `POST /webhook`, `GET /health`, gRPC-clients the kernel's `DigitalBrainGateway`. Contains
  `TelegramWebhookSetup` (registers the webhook, or resolves an ngrok tunnel for local dev),
  `TelegramUpdateForwarder` (inbound: Telegram `Update` → gateway `Send` as a generic
  `SynapseEnvelope`), `TelegramReplyDispatcher` (outbound: watches `TelegramReplyRequested`/
  `PackConfigured` signals, pulls the decrypted `telegram_token` via `GetPackConfigAsync` guarded by a
  shared `x-internal-key` header), and `SynapseStreamConsumer` (a `BackgroundService` wrapping
  `gateway.WatchSynapses` with 3s-backoff reconnect).

Hosting split (per `docs/specs/2026-07-01-distribution-and-bundles.md` §8.3, and confirmed live in
`brain/deploy/Program.cs`): the transport is **never** co-hosted in the kernel and never run from
`brain.cs` in prod — those would couple public ingress and channel scaling to the kernel's rolling
restarts. Prod IaC (`brain/deploy`, Pulumi C#) provisions a separate `digitalbrain-telegram`
ContainerApp with external HTTP ingress, alongside the `digitalbrain-jobs` kernel container app, Azure
OpenAI (`gpt-4o-mini`), storage, and observability. `brain.cs --telegram` (lines 34-43: checks
`args.Any(a => a.Contains("telegram"))`) adds the same transport project as an Aspire resource and
calls `WireTelegramTransport` — a dev-only mirror of the identical wiring, not a separate code path.

### 1.8 LLM integration: the `AskLlm` / `Signal` indirection

Packs are pure/sync (`IPackBehavior`) and cannot hold an `IChatClient` or any DI-resolved service. The
indirection exists specifically to let pack code request an async LLM call without the pack itself
doing async I/O:

`AskLlm` (`DigitalBrain.Core/Signals.cs:10-20`):
```csharp
record AskLlm(string Prompt, string ReplyType,
    IReadOnlyDictionary<string, object?> ReplyProps,
    string? ConfigPack = null, string? ConfigScope = null) : Synapse(...)
```

`LlmResponderNeuron : IHandle<AskLlm>` (`DigitalBrain.Kernel/LlmResponderNeuron.cs`) resolves an
`IChatClient` (Microsoft.Extensions.AI) — either the global DI-registered default, or, when
`ConfigPack`/`ConfigScope` are set, a per-scope client from `IScopedChatClientFactory` keyed by
`llm_provider`/`llm_key` pulled out of `IPackConfigStore` (provider strings like `"ollama"`/`"openai"`
live in pack config, cached per `(provider, key)`). After calling `GetResponseAsync(ask.Prompt)`, it
broadcasts the reply as a generic **`Signal(ask.ReplyType, props)`** — reusing the caller-specified
`ReplyType` as the signal name rather than inventing a dedicated reply synapse per use case.

`Signal` (`DigitalBrain.Core/Signals.cs:6-8`): `record Signal(string Name, IReadOnlyDictionary<string,
object?> Props) : Synapse(Name, ...)` — a generic named-event carrier. Consumers pattern-match on
`Name`: `TelegramChatNeuron : IHandle<Signal>` filters `signal.Name == "TelegramMessageReceived"`;
`TelegramReplyDispatcher` filters `TelegramReplyRequested`/`PackConfigured`. This one carrier type is
how pack-defined event names ride to both the in-app UI stream and Telegram without either transport
needing a compile-time reference to every pack's synapse types.

### 1.9 Config/secrets primitive

`PackConfigField` (`DigitalBrain.Core/Distribution/IPackBehavior.cs:6-12`): `Key, Label,
Kind(Text|Secret|Choice), Choices, DependsOnKey, DependsOnValue`. A pack declares a list of these as
`PackManifest.RequiredConfig`. Kernel auto-render: `ConfigFormSurface.Build`
(`DigitalBrain.Core/Configuration.cs:14-75`) maps each field to a `ui:TextField`/`ui:Select` node plus
a submit `ui:Button` that emits `ConfigurationProvided`, wrapped in a `ui:Screen`/`ui:Column`
`UiSurface` — no bespoke per-pack form code.

`PackConfigStore` (`DigitalBrain.Kernel/Config/PackConfigStore.cs`) encrypts **per-field**, not the
whole payload: `IDataProtectionProvider.CreateProtector(RootPurpose, scope, pack, key)` gives each
`(scope, pack, key)` triple its own protector; `SetAsync` encrypts each value individually before
JSON-serializing the ciphertext dict; `GetAsync` decrypts per-key and swallows
`CryptographicException` per-field, so one undecryptable value can't poison the rest of the config
read. Backing store is pluggable (`IPackConfigBackingStore`); the production implementation,
`AzureBlobPackConfigBackingStore`, stores each `(scope, pack)` as a block blob
`"{scope}/{pack}.bin"` in a `pack-config` container on the same Azurite-backed storage account used
for Orleans clustering/journal.

### 1.10 Known gaps / regressions

- **`start.cs` is missing from the repo but still referenced.** `AppHost.cs`, `brain.cs`, and
  `samples/QuickTest/QuickTest.csproj` (line 28, linking it as `<Compile Include="..\..\start.cs" />`)
  all reference it as "the fast in-memory path (INO + tasks + marketplace + UiSurfaces)," and
  `QuickTest.csproj:30` even claims "Canonical start.cs is at framework/start.cs" — a stale path. The
  file does not exist anywhere under `E:\digitalbraintech` (confirmed via filesystem search); `git log
  --all -- start.cs` shows it existed historically but was deleted at some point without removing the
  references. **`samples/QuickTest` would fail to build today** if this compile-item is hit.
- **A pre-existing E2E fixture bug is deferred, not fixed**: `CONTINUITY.md` documents that
  `DigitalBrainAppHostFixture.InitializeAsync` still waits on a resource literally named `"silo"` — a
  leftover from the Silo→Kernel rename — causing a 5-minute hang; the workaround is excluding that E2E
  namespace rather than fixing the wait target.
  `docs/specs/2026-06-26-bucket-d-flutter-render-e2e-design.md`-era code was the last to touch this.
- **Distribution & Bundles Phase 2 (open publishing, untrusted-code sandbox, BYO-token branded bots,
  exportable bundle file, embeddable surface) is explicitly spec-only** — `docs/specs/
  2026-07-01-distribution-and-bundles.md` §11 marks it "decide on demand," not started. Phase 0 and
  all of Phase 1 (1a manifest, 1a catalog, 1b trust, 1b facet, 1c deep-link, 1d Telegram-deploy) have
  matching commits already on `master`.
- `CONTINUITY.md`'s Bucket D entry calls out a **product gap that was found and fixed in the same
  session**, not currently open: `GatewayService.Send` originally had no `PublishToMarketplace` case,
  so publishes silently no-opped (pack code was dropped) until the E2E test caught it.
- Several "Deferred follow-ups (non-blocking)" appear scattered through `CONTINUITY.md` after most
  dated sections — e.g. trusted-publisher allowlist ergonomics, per-user vs per-install flow state,
  a possible `NeuroPack` → `Domain` rename, `ui:Gap` not being axis-aware, `ui:Sheet` missing a header
  slot. None of these block current functionality; they're backlog notes, not regressions.

---

## Part 2 — Way of Working

### 2.1 The dev loop (AGENTS.md, reformed 2026-06-21 around Musk's 5-step algorithm)

The 5 steps, in order, as stated in `AGENTS.md`: **(1) make requirements less dumb** — question every
"always/never/must"; **(2) delete**; **(3) simplify** what remains; **(4) accelerate cycle time**;
**(5) automate last**. This same framing is reused as the structuring device for the Distribution &
Bundles spec (`docs/specs/2026-07-01-distribution-and-bundles.md` §3).

Three loop speeds, escalating in cost:

1. **Fast inner loop (default)** — `dotnet build && dotnet test --filter "..."` for protocol changes,
   unit logic, step definitions, pure C# edits. This is where the large majority of authoring happens.
2. **Aspire/hosting changes** (AppHost resource graph, wiring, observability) — use the aspire MCP
   tools (`list_apphosts`, `doctor`, `execute_resource_command`, `list_console_logs`, …) or the
   `aspire` CLI; prefer targeted resource commands over a full restart.
3. **Full distributed validation** (Ollama + 3 kernel replicas + end-to-end pack embodiment/LLM flows)
   — `aspire run`, run intentionally before major PRs or when self-update/LLM flows are touched, *not*
   after every edit.

`AGENTS.md` is explicit and blunt on one point that cuts directly against generic "run everything with
high severity" instructions: **"No undefined 'high severity' rituals. Run the tests that are
relevant."** The repo's own convention is targeted `--filter` runs, not blanket full-suite runs, for
the inner loop.

Verification ritual (post-change): `dotnet build` → `dotnet test` → `aspire doctor` (MCP or CLI) →
targeted full run + feature specs "when the plan calls for it." Authoring a new bundle specifically
follows `docs/authoring-a-bundle.md`'s test-first loop: define the bundle once as pack source, drive
it with `BundleHarness` (fast, no browser) and `LiveRenderVerifier` (actual Playwright render).

Package versions are centralized in `Directory.Packages.props` with no `Version="*"` — updates are a
deliberate, separate action, not incidental to a restore. Context7 is used "when the API surface or
recent changes are unknown... not a tax on every line of code" (`AGENTS.md`) — narrower than a
blanket "always Context7 first" rule.

### 2.2 Test organization and conventions

`DigitalBrain.Tests` uses **Reqnroll** (`Reqnroll`, `Reqnroll.Tools.MsBuild.Generation`,
`Reqnroll.xUnit` in the `.csproj`) over a real Orleans `TestCluster` (referenced in 33+ files); the
shared fixture is `DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs`.

`.feature` files are flat under `Features/` — only **5 total**: `AwesomeSoftware20.feature`,
`CodeFoundry.feature`, `NeuronCore.feature`, `MarketplaceUserFlows.feature`, `TelegramExperience.feature`.
Concern-based organization instead lives
in the plain-C#-test folder structure, not the `.feature` tree: `Kernel/`, `Economics/`, `Context/`,
`Ui/`, `Mcp/`, `E2E/` (the largest, ~20 files including a `Packs/` subfolder), `Gateway/`, `Trust/`,
`Distribution/`, `Telegram/`, `Domains/`, `Foundry/`, `Sdk/`, `Awesome/`, `Company/`, `Auth/`, `Llm/`,
`Protocol/`, `Sandbox/`, `Steps/` (Reqnroll step definitions), `TestSupport/`. In practice most new
coverage is plain xUnit facts against the `TestCluster`, with Reqnroll reserved for the handful of
narrative BDD scenarios.

`E2E/` gates real browser rendering behind `[Trait("Category","E2E")]` plus either the
`RUN_FLUTTER_E2E=true` / `FAST_UI_E2E=1` env vars or `e2e.runsettings` (`brain/e2e.runsettings`) —
wiring the runsettings file once via VS Test Explorer's "Configure Run Settings" makes any E2E-tagged
test opt into the render loop with no manual env-var ceremony. CI (`.github/workflows/ci.yml`) does
not reference `e2e.runsettings` and instead excludes the gated E2E namespace by default.

### 2.3 Branch/PR conventions (observed from git log)

Commit messages follow a loose Conventional-Commits style: `feat(scope): …`, `fix(scope): …`, `docs:
…`, `infra(deploy): …`, `test: …`, `refactor: …`, `ci: …` — scopes seen include `kernel`, `core`,
`tests`, `telegram`, `gateway`, `llm`, `aspire`, `deploy`.

Feature work happens on `spec/<feature-name>` branches, each paired with a
`docs/specs/<date>-<feature-name>[-design].md` + `docs/plans/<date>-<feature-name>.md` (and, for
multi-slice specs, one plan file per phase/slice, e.g. `2026-07-01-phase1a-bundle-manifest.md`,
`2026-07-01-phase1b-trust-gate.md`, `2026-07-01-authoring-loop-slice1-flutter-freshness.md`). One real
merge is visible: `f479ce4 Merge pull request #5 from digitalbraintech/spec/telegram-llm-experience`.
Local branches present but not pushed: `spec/authoring-loop-acceleration` and its three slice
branches, `spec/telegram-llm-experience` (this one has a remote copy too). Most spec branches are
local-only — squashed/merged into `master` locally without a corresponding pushed remote branch,
matching the pattern of doing the PR dance only when it's actually useful.

`docs/` is organized in three groups: `docs/specs/` (design docs), `docs/plans/` (paired
implementation plans, often one per phase/slice), and `docs/superpowers/{specs,plans}/` (older
2026-06-26 through 2026-06-30 SDD-workflow artifacts, one directory level deeper). Top-level docs
(`docs/authoring-a-bundle.md`, `docs/ui-kit-neuron-synapse-implementation-plan.md`,
`docs/product-finalization-plan-2026-06-27.md`) sit outside both.

### 2.4 Authoring-loop tooling (what exists today for building a new bundle fast)

- **`BundleHarness`** (`DigitalBrain.Tests/Ui/BundleHarness.cs`) — embodies a bundle's shipped pack
  source in-process via the same `PackAlcEmbodier` production uses, no Aspire host, no browser.
  Seconds-scale inner loop.
- **`LiveRenderVerifier` + `DigitalBrainBrowserFixture`** — drive Playwright against the real Flutter
  RFW renderer for the "does it actually look right" render loop (tens of seconds).
- **Warm dev-cluster attach** (`spec/authoring-loop-slice3-warm-cluster`, commits `32f74f3`,
  `c20ea8c`, `1e96963`) — render E2E tests attach to an already-running `dotnet run --project
  DigitalBrain.Kernel` cluster instead of booting a fresh Aspire host per run, collapsing a 30-120s
  boot into an attach.
- **Auto-build of the Flutter web bundle when stale** (`spec/authoring-loop-slice1-flutter-freshness`,
  commit `38a3d53`) — avoids manually remembering to `flutter build web` before a render test.
- **`e2e.runsettings`** (`spec/authoring-loop-slice2-render-runsettings`, commits `e2cb38d`,
  `43207eb`) — collapses the `RUN_FLUTTER_E2E`/`FAST_UI_E2E` env-var ceremony into a one-time VS Test
  Explorer setting.

All three authoring-loop-acceleration slices have matching commits on `master`; per prior session
notes, manual cycle-time verification (actually timing the improved loop end-to-end) was still
pending as of the last recorded check.

### 2.5 Verification ritual, summarized

```
dotnet build
dotnet test --filter "<relevant category/namespace>"   # inner loop, default
aspire doctor                                            # after AppHost/wiring changes
aspire run                                               # full distributed validation, intentionally, not per-edit
```

Use `dotnet test` without a filter, and `aspire run` for full E2E, only when the plan explicitly calls
for it (major PR, self-update or LLM-flow changes) — not as a blanket post-edit ritual.
