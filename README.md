# NeuroOS / DigitalBrain — Framework (brain/)

**NeuroOS** is the canonical .NET Aspire + Orleans runtime ("Kernel") for **DigitalBrain**. Everything is a **Neuron** (actor grain) or **Synapse** (immutable typed message). Server-driven UI, marketplace packs, typed C# only (`.ino` is dead).

The web client lives at [digitalbrain.tech](https://digitalbrain.tech) and talks to the kernels via gRPC + RFW/ForUI surfaces.

## What lives here

- `DigitalBrain.Core` — pure protocol: `INeuron`, `Synapse`, `IHandle<T>`, `UiSurface`/`UiWidgetTree`, `NeuronId`/`TaskId`, `IPackBehavior`, marketplace seeds.
- `DigitalBrain.Kernel` — the runtime (Orleans + services): base `Neuron`, embodiment (`Foundry`/`PackAlcEmbodier`), LLM, economics (Stripe + ECDSA), context/memory (hybrid + Qdrant), server-driven UI (UiSurface emission + bidirectional `UiGateway`), self-update/HA rolling.
- `DigitalBrain.Aspire` — hosting SDK (`AddDigitalBrain`, `WireKernelSilo`, `AddFlutterClient`...).
- `NeuroOSPrototype.AppHost` — the Aspire model (3× kernel replicas, Ollama, Azurite, gateway, MCP, flutter-ui).
- `DigitalBrain.Gateway` — ASP.NET entry + gRPC.
- `DigitalBrain.Mcp(.Tools)` — internal MCP server (neuron tools).
- Tests (Reqnroll BDD over real `TestCluster` + Aspire E2E).

See `Brain.slnx`, `aspire.config.json`, `Directory.Packages.props` (Aspire 13.4.6, Orleans 10.2, net11.0).

## Core Concepts

- **Neuron** = Orleans grain (`IGrainWithStringKey` implementing `INeuron` + `IHandle<>` for the synapses it consumes).
- **Synapse** = broadcast or point-to-point message (`[GenerateSerializer] record : Synapse`). Carries `SynapseId` + causation lineage via `Stamp(...)`.
- **NeuroPack** = signed (ECDSA-nistP256) C# code. Compiled in collectible ALC, embodied as running behavior. Marketplace install reaches N+1 handlers without restart.
- **UI** = `UiSurface : Synapse` (or `RfwCard`). Neurons emit `UiWidgetTree` using the official kit (`NeuronUiKit`, `Ui`). Client is thin (ForUI + RFW host/renderer). Shell, nav, content, experiences all come from neurons.

## Working in this repo (AGENTS.md loop)

1. Make requirements less dumb.
2. Delete (target >10% net reduction).
3. Simplify.
4. Accelerate (fast targeted feedback).
5. Automate last.

**Fast inner loop**: `dotnet build && dotnet test --filter "..."` (protocol/unit/step/UI contracts).

**Aspire/hosting**: use MCP tools (`aspire__doctor`, `aspire__list_resources`, `aspire__execute_resource_command` to restart flutter-ui/kernel, `aspire__list_console_logs`) or `aspire` CLI. Prefer targeted commands.

**Full distributed**: `aspire run` (Ollama + 3 kernels + client) only when needed for end-to-end (pack embodiment, LLM flows, live surfaces).

**After every change**:
- `dotnet build`
- Relevant `dotnet test`
- `aspire doctor` (MCP or CLI)
- Flutter: `flutter analyze` + targeted tests on renderer/kit
- MCP resource restart + logs when UI or hosting touched

**Rules**:
- Context7 for all library/framework APIs before writing against them (ForUI, RFW, Orleans, Aspire, gRPC...).
- Relative paths only. Never reference `C:\Users\...`.
- Meaningful names over comments. No vacuous `/// <summary>`.
- Central versions in `Directory.Packages.props`. Latest deliberate.
- Self-explanatory variable names.

## UI Kit (Neurons + Synapses focus)

See `docs/ui-kit-neuron-synapse-implementation-plan.md` (this plan in execution) and `CONTINUITY.md` (2026-06-27 neuron-UI phase).

- Grammar lives in `DigitalBrain.Core/UiSurfaces.cs` (`NeuronUiKit`, `Ui`, `UiWidgetTree`, `UiSurface.ForWidgetTree`...).
- Experiences: `KitExperience` + fluent `UiExperience` (packs author multi-hop UIs in pure C#).
- Emission examples: `UserSessionNeuron` (app-shell), `SystemNeurons`, live data helpers.
- Wire: `HomeFeedBus` + `UiSurfaceRfwBridge` + bidirectional `UiGatewayService`.
- Client: `rfw_host/` (host + `UiSurfaceTreeRenderer`) + `ui_kit/` (thin ForUI impl of `ui:` vocab) + ForUI shell. Thin host only.

All shell/nav/content/forms/actions upgradable by publishing + installing packs.

## Quick start (dev)

From `brain/`:
```sh
aspire doctor
# targeted work
dotnet build DigitalBrain.Core/DigitalBrain.Core.csproj
dotnet test --filter "UiSurface|KitExperience"
```

From `app/`:
```sh
flutter pub get
flutter run -d chrome   # or windows
```

Full stack (kernels + Ollama + gateway + flutter):
```sh
aspire run
```

See `samples/`, `DigitalBrain.Tests/`, `NeuroOSPrototype.AppHost/AppHost.cs`.

## Deploy / Ops

See `deploy/`, Pulumi files, `docs/`.

## Related

- `app/` — Flutter client (static bundle on GitHub Pages).
- `core-requirements/` — planning (Musk approach, project survey).
- `Projects/` — archive (read for patterns, do not extend).
- `marketplace/` — seeds & client.

License, continuity, and session notes live alongside the code. Follow the 5 steps.

---

*NeuroOS: typed C# neurons, synapses on a timeline, UI from the kernel.*