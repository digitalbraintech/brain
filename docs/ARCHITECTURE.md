# DigitalBrain — Current Architecture (2026-09-05)

**Authority now:** the neuron/synapse/signal graph plus per-input C# behavior programs
(`CONTEXT.md`, `Get<T>()`, typed subscriptions and `SaveScriptAsync`). The older
durable-runs, durable-scripting and admission designs are superseded.

Code is the source of truth. The current implementation contract and verification
record are in `programmable-behaviors-implementation.md` and
`programmable-behaviors-validation.md`.

## Product

Multiuser chat product. One kernel image, one Flutter codebase, entities all the
way down. Core differentiator: **the neuron substrate itself** — a durable, weighted
graph of neurons connected by synapses. Broadcast follows the source neuron's
existing synapses; weight orders connections and prunes Learned ones. Saved C# handlers run out of process; they
`Send` typed signals and write entities. No runtime-generated Orleans types and no
second, English-language runtime.

## Core loop (chat + dynamic UI)

1. Flutter → `Get<IChat>().RequestAsync(SendMessage)` → durable turn → `ChatTurnWorker`.
2. Worker → `IAssistant` → a Microsoft.Extensions.AI `IChatClient` (the
   configured default model or an explicit marker) with an AI toolset.
3. Every UI-kit component registers an AI tool (`render_chart`,
   `generate_image`, …) via `TurnBoundFunction`. A tool call creates/updates an
   `Entity<TState>` and posts a **reference card** `{componentKind, entityId,
   caption}` into the transcript — never a snapshot.
4. SSE pushes the card; Flutter mounts the matching kit widget, which reads the
   entity via its `[ClientEntryPoint]` contract. The same entity renders
   full-size on a Surface — one live state, two mounts.
5. Interactive components (Button, Form, Todo) fire their command signal back
   through the same path a user message takes.
6. Every component state record lands in `flutter-wire-contracts.golden.json`;
   a conformance test fails on C#↔Dart drift.

## UI kit — 13 components, one wave

All follow the identical template: `Entity<TState>` + `[ClientEntryPoint]`
contract + Flutter widget + golden wire contract. Binary payloads (Image, File)
live in Azure Blob Storage; the entity holds the reference.

MarkdownCard, Chart, Image (multi-provider generation), Button, Table, Form,
TodoList, CodeCard, ProgressCard, TimerCard, Browser (embedded webview),
FileCard, Diagram.

The existing `digitalbrain_ui_kit` Dart package (KitChartPart, KitButtonPart,
KitGalleryScreen) is the starting point for the widget side.

## AI module

- **Providers**: port IAW's provider-factory layer (E:\intochat\Projects\IAW)
  behind DigitalBrain's marker DSL — AppHost declares `WithLlm<IModel>()`, each
  marker maps to a keyed MEAI `IChatClient`. Providers: OpenAI (top-3 current
  flagship line), Anthropic (Opus/Sonnet/Haiku current), Google (Gemini
  Pro/Flash current), xAI (Grok). Exact model ids pinned against provider docs
  at implementation time.
- **Segregation** (no tiers — vetoed): model markers are pure types
  (`IOpus5 : ILLM`) that select keyed `IChatClient`s; agents (`Agent` base:
  instructions + tools, model chosen via `[Llm<TModel>]`) are the only
  conversational citizens. The unkeyed default client follows
  `DigitalBrain:AI:Default:Model`, else the first configured provider (cloud
  before local).
- **Local dev**: Ollama `IGemma4` + `IEmbeddingGemma` stay so dev and CI run
  offline. Production embeddings come from a cloud provider; embedding
  dimensions are config-driven because Qdrant index dims lock to them. Every semantic
  index generation also pins provider/model identity, dimensions, preprocessing, and
  document-format version; migrations build and validate a new generation before cutover.
- **Agent layer**: `Agent` neurons over MEAI clients. Each turn prepares tools
  through one asynchronous `IAgentToolSource` contract. Ino delegates to specialist
  neurons through ordinary source-owned requests; no additional orchestration
  runtime is required.
- **Voice**: Whisper STT (Foundry Local) stays dev-only. PersonaPlex is deleted
  (see Trash record); future voice = provider realtime APIs.

## Authored applications

User- and assistant-authored C# runs through `DigitalBrain.Scripting`. The authoring service stores exact source revisions per owner and principal, validates them without business execution, and publishes content-addressed artifacts. Activation applies a verified revision. The hosted supervisor starts separate artifact worker processes and retains revisions still referenced by admitted work.

An application declares commands, typed input handlers, state, durable delays, event waits, output ports, and apply-time composition with `brain.Application(key)`. The kernel durably records definitions, accepted operations, revision pins, checkpoints, effect intents, and waiting state. Workers use one-use bootstrap tickets and server-issued capabilities bound to owner, principal, application, and revision. Current capability authority is silo-local; multi-silo issuance and revocation remain a separate guarantee.

Application Studio, assistant tools, and MCP use the same `IApplicationAuthoring` contract and persisted source store. The graph displays actual neuron subscriptions and journal activity; it does not provide a second scripting runtime. See [Getting started](GETTING_STARTED.md) for the current file format and workflow, the [design specification](superpowers/specs/2026-09-07-file-based-scripting-design.md) for intended semantics, and [recorded validation](programmable-behaviors-validation.md) for tested guarantees and remaining limits.
## Integration modules

Microsoft, Google, and Salesforce use the same Contracts / implementation /
Aspire.Hosting boundaries. `IAspire`, `IGmail`, and `ISalesforce` inherit `IAgent`:
one `AgentRequest`/`AgentReply` contract. Ino sees delegation tools; each specialist
owns its native discovered MCP catalog, instructions, and provider policy.

`Sdk/Mcp` owns isolated sessions, native catalog snapshots, STDIO/HTTP transports,
binding revision checks, bounded results, and the single known-read 401 retry.
It never automatically replays a write or an uncertain operation. The shared AI
tool boundary handles screened/redacted evidence and safe failure categories.
Provider modules retain account, query, consent, and confirmation policy without
handwriting replacement MCP schemas.

`Sdk/OAuth` owns the one-use `BrowserLogins` registry, callback surface and completion
worker. Google and Salesforce tokens remain volatile and private to their
principal-bound connection stores. GitHub persists nonsecret authorized repository
bindings, uses installation credentials for unattended reads, and requires signed
webhook reachability and known CI requirements before monitoring is ready. Its
user OAuth token is used to establish the authorized repository intersection.

Login completion resolves a stored exact specialist request, native read allowlist,
and current connection revision. The existing chat worker resumes that target once;
restricted continuation cannot delegate elsewhere or execute writes. Gmail draft
creation and Salesforce create/update use exact published previews and a fresh
authenticated user confirmation. Gmail send/delete and Salesforce delete remain
outside the admitted catalog.

Each module's hosting project retains its operator parameters (`WithGmail()`,
`WithHostedMcp()`, `WithAspire()`). Static module-owned presentation descriptors
provide labels and icon keys for observed neurons; they never create graph topology.
`AgentActivity` is journal evidence, not an automatic broadcast to subscribers.

## Multiuser

- Login + password only (registration included). `UserAccountEntity` per user:
  username key, password hash in state. No external IdP, no EF, no relational
  DB.
- Sessions: ASP.NET Core cookie (web, same-origin) **and** bearer token issued
  at login — both from day one so native clients never depend on cookies.
- `OwnerId` keying and `VerifiedActor` (already in the kernel) carry identity
  through the grain graph.

## Deployment

- **Product = one Docker image** (kernel, built from
  `src/Kernel/DigitalBrain.Silo/Dockerfile`) published to Docker Hub. The
  built Flutter web app is baked into this image and served by the kernel —
  same-origin cookies and SSE by construction.
- **Runtime**: Azure Container Apps pulls the image. Scale = silo replicas.
- **Secrets**: Azure Key Vault → injected env (`DigitalBrain__*` keys already
  stubbed in docker-compose.yml).
- **State**: Azure Storage (Orleans clustering / grain state / journaling /
  reminders + blobs for Image/File components). Qdrant runs as an external
  container with a persistent volume.
- **No GPU in production**: Ollama / Whisper / Foundry Local are dev-only;
  cloud providers serve all production inference.
- **Clients**: web ships in the image; Windows/mobile are Flutter build targets
  against the same API using the existing `cookie_http_client` or the bearer
  token.

## Trash record (deleted 2026-08-22, recoverable from git history)

- PersonaPlex, entirely: `src/Modules/AI/PersonaPlex`, contracts, hosting
  extensions, kernel WebSocket endpoint + protocol, `src/Runtime/PersonaPlex`
  python runtime, Flutter voice client/protocol/screens/controller, the shell's
  Voice destination, `flutter_soloud` dependency and web bootstrap scripts, all
  PersonaPlex tests, and the PersonaPlex-era plans/specs/research docs.
- Rationale: the runtime never worked (branch history ends "Not working 2" /
  "Disable personaplex"), needed a GPU the deployment target doesn't have, and
  8 of its E2E tests were failing at HEAD.

## Build order

1. AI providers (IAW port, no tiers) — shipped 2026-08-22.
2. Auth (UserAccountEntity, cookie + token) — multiuser boundary.
3. UI kit, all 13 components on the template. (template + Chart + Image shipped 2026-08-23)
4. Self-knowledge catalog — historical; not in the current product path.
5. Durable authored applications with immutable file artifacts and supervised worker processes.
6. Google + Salesforce specialist neurons through the inherited generic agent request contract.
7. Image → Docker Hub, ACA + Key Vault deploy.
