# DigitalBrain — Distribution & Bundles Product Spec

- **Status:** Draft for review
- **Date:** 2026-07-01
- **Owner:** Vladyslav Horbachov (creator #1)
- **Repo:** `digitalbraintech/framework` (`brain/`); touches `digitalbraintech/app` (`app/`)
- **Supersedes:** nothing — this is the first consolidated product definition for how software is *packaged, distributed, hosted, and iterated on* in DigitalBrain.

---

## 0. TL;DR

DigitalBrain is a platform for shipping **small AI experiences** that are written as typed C#, compiled and embodied live into a running brain (one broadcast reaching N+1 handlers with no restart), and consumed through server-driven UI and chat channels.

This spec defines the **creator loop** as the product's center of gravity:

> **author → test (rendered live in UI) → publish → discover → install → configure → use → monetize.**

The shareable, installable unit is a **Bundle**: a `NeuroPack` plus a thin manifest. The first delivery channels are the **in-app UI** and a **single platform Telegram bot** with deep-link routing. In v1, **publishing is gated to trusted/signed authors**; running arbitrary third-party code safely (sandbox + open publishing) is an explicit Phase 2 problem we are deliberately *not* solving yet.

Everything here is built on what already exists — the protocol core, the pack rail, the marketplace neuron, server-driven UI, the Telegram transport, and the ACA/Pulumi deploy. The new work is a **manifest layer**, a **blessed test-first authoring loop**, **deep-link sharing**, and **making the Telegram channel a first-class deployed app** rather than a dev-only afterthought.

---

## 1. The bet, and who this is for

### 1.1 The primary actor

The main character is the **author** — first you, then a small set of trusted creators — who builds a tiny AI experience and ships it to end-users. End-users consume through in-app UI and Telegram; they are first-class but they are not who the *distribution machinery* is optimized for in v1.

### 1.2 Why A-first (creator marketplace) over a Telegram-first consumer wedge

We explicitly considered three product bets:

| Bet | Core loop | Why not v1 primary |
|---|---|---|
| **A — Creator marketplace** (chosen) | author → publish → discover → install → use | — |
| B — Telegram-first end-user | discover → configure → use → share | Best *reach*, but optimizes for a consumer wedge before the authoring substrate is proven; folded in as the Telegram **channel**, not the center. |
| C — Personal brain extension | write → install into my brain → it works | Not a competitor — it's the **substrate** (Phase 0) both A and B stand on. |

The decisive reasons for A:

1. **The architecture is already a creator platform.** Packs, ECDSA signing, commission, licenses, runtime compilation, and N+1 embodiment are creator-economy primitives. A-first builds on rails that exist; it invents the least.
2. **Cold-start is defused, not ignored.** The classic two-sided-market trap (no creators ↔ no users) does not bite here because **you are creator #1 and the shelf is already seeded** with first-party bundles (`ui-kit`/ForUI, Workbench, Graph3D, Telegram responder, hello-world, ui-gallery). The author gets value *using their own bundles* before any crowd arrives.
3. **It keeps the consumer wedge as a channel.** Telegram doesn't disappear — it becomes the highest-reach *channel* a content bundle can target, with deep-link sharing. We get B's distribution graph without making B the product.

### 1.3 Non-goals for v1

- No open/public third-party publishing (publishing is invite/trust-gated).
- No untrusted-code sandbox (we run only trusted/signed authors in the shared brain).
- No per-creator branded Telegram bots (single platform bot + deep-link routing).
- No embeddable/iframe surface.
- No exportable bundle file (Phase 1.5).

---

## 2. The problems distribution must solve

Distribution is not one problem — it is a chain of ten. Naming each link lets us decide which matter and delete the rest. The right-hand column is the v1 decision.

| # | Problem | v1 decision |
|---|---|---|
| 1 | **Authoring → live** — get from "I wrote a test + C#" to "it runs and I can see it" | Blessed test-first loop that renders the bundle live in UI as the test runs (Phase 0). |
| 2 | **Packaging** — what is the shareable artifact and what's inside | `NeuroPack` + manifest (entry experience, config, channels, deps). One wire format. |
| 3 | **Discovery** — how someone finds a bundle | In-app marketplace surface (faceted) + per-bundle deep-link. |
| 4 | **Acquisition / install** — landing in a brain and handling synapses (N+1) | Already built; unchanged. |
| 5 | **Configuration** — per-install secrets/choices without redeploy | Manifest config schema → `ConfigFormSurface` → pack config store. Built. |
| 6 | **Channel delivery** — how a human reaches the experience | In-app SDUI (primary) + Telegram (platform bot + deep-link). Channel-agnostic bundle logic. |
| 7 | **Trust & safety** — signing, capability gating, untrusted code | ECDSA signing + CapabilityGate (built). Publishing **gated to trusted authors**; sandbox deferred. |
| 8 | **Monetization** — price, commission, licenses | Built; author sets price, platform takes commission, license gates premium install. |
| 9 | **Updates / versioning** — moving an installed bundle to a new version | Versioned packs re-embody live; kernel self-updates via rolling HA restart. Built. |
| 10 | **Hosting — whose brain, where** | One shared kernel ×N in ACA; Telegram as a separate stateless ACA channel app. |

---

## 3. Applying Musk's 5 steps

This spec is structured so every surviving decision can be traced through the algorithm in `core-requirements/Musk approach.txt`.

### Step 1 — Make the requirements less dumb

Requirements we questioned and their verdict:

- **"We need a `Bundle` type."** → *Dumb as stated.* A new first-class type means a whole new install/trust/version/test rail to build and prove. **Deleted** in favor of `NeuroPack` + a manifest. The pack rail already compiles, signs, embodies, and proves N+1.
- **"The marketplace must be open to the public for v1."** → *Dumb.* Open publishing forces the untrusted-code-sandbox problem, which is the single hardest thing in the system. **Deleted** for v1 → trusted-publisher gate.
- **"Telegram should run from `brain.cs` in prod / or be co-hosted in the kernel."** → *Dumb.* Co-hosting puts public ingress on the silo and couples channel I/O to kernel scaling and rolling restarts; `brain.cs` is a dev launcher, not a prod host. **Resolved** → separate stateless channel app.
- **"Each bundle needs its own branded bot."** → *Premature.* With a handful of trusted creators, N branded bots is multiplexing work with no v1 payoff. **Deferred** → single platform bot + deep-link routing.

### Step 2 — Delete the part or process

Deleted from v1 scope: new Bundle runtime type, open publishing, untrusted-code sandbox, per-creator bots, exportable file, embeddable surface, the legacy diagnostic gateway (kernel hosts gRPC directly). If we don't add ~10% of these back later, we didn't delete hard enough — and several *are* explicitly scheduled back in Phase 2.

### Step 3 — Simplify / optimize what remains

The manifest is additive metadata on an existing record. The Telegram channel is one token and a routing table. Discovery is facets over the existing marketplace cache. Nothing remaining is net-new infrastructure.

### Step 4 — Accelerate cycle time

The Phase 0 authoring loop *is* cycle-time work: a bundle goes from edited C# to rendered-in-UI inside a single `dotnet test` run. This is the product's moat and the team's daily loop.

### Step 5 — Automate

Automated last: publish-on-green (a bundle whose tests pass can be published with one command), and deep-link generation. We do **not** automate review/trust in v1 — trust is a human gate while the creator set is small.

---

## 4. Core business components (the product taxonomy)

Everything is a Neuron or a Synapse. On top of that protocol, the product is composed of **bundles in three tiers**. This is the concrete answer to "kernel bundle / ui bundle / telegram bundle."

```
┌─────────────────────────────────────────────────────────────┐
│ CONTENT bundles        the micro-apps creators ship          │
│   hello-world, color-picker, travel-planner, <your app>      │
├─────────────────────────────────────────────────────────────┤
│ CHANNEL bundles        add a delivery surface                │
│   telegram, web                                              │
├─────────────────────────────────────────────────────────────┤
│ SUBSTRATE bundles      the platform itself, shipped as packs │
│   kernel (self-updating), ui-kit (39 ForUI covers)           │
├─────────────────────────────────────────────────────────────┤
│ PROTOCOL core          not a bundle — the law                │
│   Neuron, Synapse, IHandle<T>, IPackBehavior, manifest types │
└─────────────────────────────────────────────────────────────┘
```

### 4.1 Protocol core (`DigitalBrain.Core`)

Not a bundle — the dependency-light law every bundle is written against: `INeuron`, `Synapse`, `IHandle<T>`, `NeuronId`/`TaskId`, `IPackBehavior`, the marketplace/trust/UI contracts, and (new) the **bundle manifest types**. Stays dependency-light because `Mcp.Tools` references only Core.

### 4.2 Substrate bundles

- **`kernel`** — the Orleans runtime (formerly "Silo"). Already shippable and self-updating as a pack via the rolling-HA-restart path. The kernel is the host every other bundle embodies into.
- **`ui-kit`** — the server-driven UI vocabulary: the 39 ForUI covers (`ui:*` registry) plus the RFW host. Every experience draws on it. First-party, always installed.

### 4.3 Channel bundles

- **`telegram`** — turns `TelegramMessageReceived` into bundle dispatch and bundle output into Telegram replies. The *transport* (webhook ingress, bot API egress) is infrastructure; the *logic* (e.g. the seeded `Telegram.Responder`) is a pack.
- **`web`** — the in-app server-driven UI session (`UiGateway` bidirectional gRPC + `HomeFeedBus`). The default, always-present channel.

### 4.4 Content bundles

What creators actually ship. A content bundle is a `NeuroPack` whose manifest declares one entry experience, its config, and which channels it targets. Seeded examples already exist (hello-world, ui-gallery, color-picker).

---

## 5. The Bundle model

### 5.1 Definition

A **Bundle** is a `NeuroPack` (unchanged wire format — name, version, typed-C# `Code`, owner, ECDSA signature, price, commission) plus a **manifest** that makes it a *product* rather than a bare capability.

The manifest extends the existing `PackManifest` (which today declares `HandledSynapseTypes` + `RequiredConfig`) with product fields. Proposed shape, living in `DigitalBrain.Core` next to `IPackBehavior`:

```csharp
public record BundleManifest(
    IReadOnlyList<SynapseType> HandledSynapseTypes,
    IReadOnlyList<PackConfigField>? RequiredConfig,
    BundleTier Tier,                               // Substrate | Channel | Content
    ExperienceRef? EntryExperience,                // the journey a user lands on
    IReadOnlyList<BundleChannel> Channels,         // InApp | Telegram | Web
    IReadOnlyList<BundleDependency>? Dependencies); // other bundles this one needs

public enum BundleTier { Substrate, Channel, Content }

public enum BundleChannel { InApp, Telegram, Web }

public record ExperienceRef(string ExperienceId, string EntryEvent = "start");

public record BundleDependency(string PackName, string MinVersion);
```

Design rules:

- **Backward compatible.** A pack with no product fields is a valid degenerate bundle (`Tier = Content`, no entry experience, in-app only). Existing packs keep working.
- **Channel-agnostic logic.** A bundle never references a transport. It declares `Channels` and emits channel-neutral synapses/surfaces; the channel bundle adapts them.
- **Dependencies resolve at install.** Installing a bundle whose `Dependencies` are unmet either auto-installs them (if trusted) or fails with a clear missing-dependency surface. No transitive runtime magic — dependencies are other installed bundles handling their own synapses (the N+1 model already gives us composition for free).

### 5.2 What we deliberately did *not* build

- No separate `Bundle` grain, install path, trust path, or version store. A bundle *is* a pack everywhere it already mattered.
- No dependency version solver beyond `MinVersion` — bundles are small; deep transitive graphs are a smell, not a feature.

---

## 6. The authoring & test-first iteration loop (Phase 0 — the moat)

This is the product's differentiator and the requirement you weighted most: **bundles are small by nature, so they're visible instantly in UI during the test.**

### 6.1 The blessed loop

```
1. Write a .feature scenario (or xUnit fact) describing the experience.
2. Write the bundle's C# (IPackBehavior + experience hops).
3. `dotnet test --filter <bundle>`:
     - boots an Orleans TestCluster brain
     - publishes + installs the bundle (ALC embodiment, N+1 proven)
     - fires the experience synapses
     - asserts the emitted UiSurface / RfwCard
     - streams the surface to a live Flutter view (HomeFeedBus -> Playwright)
       so you WATCH it render as the test runs
4. Green -> the bundle is publishable.
```

This already exists in skeleton and must become the blessed, documented path:

- `HomeFeedBus.Subscribe()` makes surfaces observable synchronously inside a test (proven by `ExperienceStepDispatchTests`).
- `LiveRenderVerifier` + `DigitalBrainBrowserFixture` drive Playwright against the real Flutter renderer (proven by `HelloWorldRendersE2ETests`).
- `HandlerGrowthTests` / `PackBroadcastReactivityTests` prove the N+1 install semantics every bundle relies on.

### 6.2 Two speeds

- **Inner loop (seconds):** `dotnet build && dotnet test --filter` over the TestCluster. Asserts surfaces from `HomeFeedBus` without a browser. This is where 95% of authoring happens.
- **Render loop (tens of seconds):** the same test with the Playwright fixture attached, so you literally see the bundle render. Run before publishing or when the UI shape is in question.

### 6.3 Authoring surfaces

- **C# authoring (v1):** the canonical path — typed C#, file-based launchers (`start.cs`, `brain.cs`), the test harness above.
- **In-app authoring (later):** the MCP tools (`run_closed_loop`, `ask_ino`, `publish_to_marketplace`) and Foundry already let a bundle be generated and published from inside a brain. This is how non-C# creators eventually author — Phase 2 territory, but the rails exist.

---

## 7. Distribution model, end to end

### 7.1 Publish

A trusted author publishes a signed bundle via `PublishToMarketplace` (CLI/MCP/UI). The marketplace neuron caches it and emits a refreshed marketplace surface. **Publish is trust-gated:** the publishing path requires a trusted/known author identity; unsigned or unknown-author publishes are refused in v1.

### 7.2 Discover

The in-app marketplace surface lists bundles faceted by **tier**, **channel**, and **category**, with search over name/description. Each bundle has a stable id used for deep-linking. Discovery is seeded by first-party bundles so the shelf is never empty.

### 7.3 Install → N+1

`InstallFromMarketplace` runs the existing gates (ownership for private, signature verification, license entitlement for priced bundles), records commission, fires `NeuroPackInstalled`, and routes to the `GeneratedNeuron`, which compiles → embodies in a collectible ALC → handles its declared synapses. One broadcast now reaches N+1 handlers, no silo restart.

### 7.4 Configure

If the manifest declares `RequiredConfig`, install emits a `ConfigFormSurface`. The user fills it; `ConfigurationProvided` stores values in the pack config store keyed by `(packName, scope)`. The bundle reads config at runtime. No redeploy — this is how a Telegram bundle gets its token/keys.

### 7.5 Channel delivery

The bundle emits channel-neutral output. The **web channel** (default) streams `RfwCard`s via `HomeFeedBus` → `UiGateway` → Flutter RFW host → `ui:*` registry. The **Telegram channel** adapts the same output to bot messages (§8).

### 7.6 Sharing (v1)

- **Marketplace install** — the baseline in-app rail.
- **Deep-link** — every bundle/experience has a shareable link: a **web URL** (`/#/experience/<bundle>/<experienceId>`) and a **Telegram deep-link** (`https://t.me/DigitalBrainBot?start=<bundleId>`). This is the viral surface and the reason Telegram earns its place.
- *(Phase 1.5)* exportable signed bundle file for offline/peer import.
- *(Phase 2)* embeddable surface (iframe/SDK).

### 7.7 Monetize

Author sets `Price`; platform takes `CommissionRate`; priced installs require a license entitlement (`ILicenseNeuron`). All built today; works at N=1.

### 7.8 Update

Publishing a higher version and installing it re-embodies the bundle live. The `kernel` substrate bundle updates itself via the rolling HA restart (`PerformKernelSelfUpdate` → `AspireNeuron`). Content/channel bundles never need a silo restart.

---

## 8. Telegram channel design

### 8.1 Identity & routing (v1)

- **One platform bot** — `@DigitalBrainBot`, one token held by the platform.
- **Deep-link routing** — a user opens the bot with `?start=<bundleId>`; the channel binds that chat to that bundle/experience for the session. This is also the share surface (§7.6).
- **Logic lives in packs.** The bot's behavior for a given bundle is the installed content bundle handling `TelegramMessageReceived` and emitting channel-neutral output (e.g. `AskLlm` → `TelegramReply`). The transport never contains business logic.

### 8.2 The transport (dumb pipe)

The existing `DigitalBrain.Telegram.Transport` ASP.NET service stays a thin pipe:

- **Inbound:** `POST /webhook` validates the Telegram secret header, maps the update to a `TelegramMessageReceived` signal (`chatId`, `fromUserId`, `text`, plus the resolved `bundleId` from deep-link state), and forwards it to the kernel over gRPC `Send` with the `x-internal-key` service secret. Fire-and-forget ack.
- **Outbound:** subscribes to the kernel synapse stream, filters `TelegramReply`, calls the bot API.

### 8.3 Hosting — resolving the controversy

The question was: *in prod, does Aspire deploy the bot as a new app service, or does it run from `brain.cs`?* The answer:

> **Neither co-hosted in the kernel nor run from `brain.cs` in prod. The Telegram transport is a first-class, separately-deployed, stateless ACA channel app.** `brain.cs --telegram` is the *dev mirror only* — it runs the identical transport locally so you develop against the real wiring.

Rationale: a channel transport has a public ingress, a different scaling profile, and must survive kernel rolling restarts. Co-hosting violates all three; `brain.cs` is a launcher, not a host. Keeping it a separate stateless app means it scales independently, can boot no-op without a token, and pulls its token from the pack config store after configuration — exactly the current transport behavior, now promoted to a deployed prod resource.

---

## 9. Hosting & deployment model

### 9.1 Two environments, one resource graph

| Resource | Dev (Aspire `aspire run` / `brain.cs`) | Prod (ACA via Pulumi) |
|---|---|---|
| **Kernel** | `DigitalBrain.Kernel` ×3 HA replicas | Container App, 1–5 replicas, external ingress (HTTP/gRPC) |
| **LLM** | Ollama (Qwen 2.5-coder) container | Azure OpenAI (`gpt-4o-mini`) |
| **Storage** | Azurite (clustering/grainstate/journal) | Azure Storage (Tables + Blobs) |
| **Web/Flutter** | `flutter run -d windows` executable | Static web bundle on GitHub Pages (`digitalbrain.tech`) |
| **Telegram** | `brain.cs --telegram` (dev mirror) | **Separate stateless ACA channel app** (new in this spec) |
| **MCP** | co-hosted on kernel Kestrel | internal-only |
| **Observability** | OTel → Aspire dashboard | OTel → App Insights / Log Analytics |

### 9.2 The v1 hosting gap to close

Prod IaC (`brain/deploy` Pulumi) today provisions only the kernel container app, Azure OpenAI, storage, and observability. **This spec adds the Telegram channel app to the Pulumi graph** as a second container app wired to the kernel's internal gRPC endpoint with the `x-internal-key` secret and the bot token parameter. The Flutter web bundle continues to deploy to Pages on push to `main`.

### 9.3 Tenancy (v1)

One shared kernel cluster runs all bundles. Per-user/per-session isolation is by **grain-key namespacing**, not separate silos. This is sufficient for *data* isolation and acceptable *only because* code execution is trust-gated (§10). It is **not** a malicious-code boundary — see Phase 2.

---

## 10. Trust, safety, and tenancy

### 10.1 v1: trusted-publisher gate

- **Signing:** ECDSA-nistP256 over `Name|Version|SHA256(Code)|PublicKey` (built). Verifies integrity, not authorship trust by itself.
- **Trust gate:** publishing requires a **trusted author identity** (you + approved creators via `TrustedPublisher`). The shared brain runs only this trusted set.
- **CapabilityGate:** rejects banned namespaces (`Process`, `Reflection.Emit`, `InteropServices`, `Runtime.Loader`, `Win32.Registry`). A guardrail against accidents — **explicitly not** a security jail against a malicious trusted-author. Acceptable in v1 because the author set is small and known.

### 10.2 Phase 2: opening up safely

Opening publishing to the public requires a real isolation boundary for untrusted compiled C#. Documented options, to be specced when we get there:

- **Per-tenant isolated brains** — each public creator/app in its own silo/ACA app or namespace; arbitrary code contained to its tenant.
- **Shared brain + real sandbox** — process/container isolation, resource limits, no shared state for embodied packs.

We pick one when public publishing is a real demand, not before.

---

## 11. Phased build decomposition

Each phase is independently shippable and gets its own implementation plan (writing-plans) when started.

### Phase 0 — Authoring loop (the moat)

- Promote the test-first + live-render loop to the blessed, documented path.
- Harden `HomeFeedBus` subscription, `LiveRenderVerifier`, and the Playwright fixture into a one-command "watch my bundle render" experience.
- Author-facing docs: "write a bundle in 15 minutes."
- **Exit criteria:** a new content bundle goes from empty file to green test with live-rendered UI in one `dotnet test` run.

### Phase 1 — Bundle manifest + gated marketplace + Telegram channel

- **1a — Manifest:** add `BundleManifest` to `DigitalBrain.Core`; backward-compatible; surfaced in publish/install.
- **1b — Gated publish + faceted discovery:** trust-gate the publish path; add tier/channel/category facets + search to the marketplace surface.
- **1c — Deep-link sharing:** web URL + Telegram `?start=` resolution.
- **1d — Telegram as a deployed channel:** add the transport to the Pulumi ACA graph; master bot + deep-link routing; token via pack config.
- **Exit criteria:** you publish a signed content bundle, share its Telegram deep-link, a fresh user opens the bot and uses the experience, all in prod.

### Phase 2 — Open up

- Untrusted-code isolation (per-tenant brains or sandbox — decide on demand).
- Public/open publishing with review.
- BYO-token branded bots (multiplexed transport).
- Exportable signed bundle file (pull forward to 1.5 if needed) + embeddable surface.

---

## 12. Success criteria

- **Authoring velocity:** time from "new bundle idea" to "green test with live-rendered UI" < 30 min for a small bundle.
- **Distribution proof:** a signed content bundle published, shared by Telegram deep-link, and used by a fresh user in prod — end to end, no manual deploy.
- **No-restart guarantee:** installing/updating a content or channel bundle never restarts the silo (kernel self-update is the only restart path, and it's rolling/HA).
- **Trust integrity:** no untrusted author code runs in the shared brain in v1.

---

## 13. Open questions (tracked, not blocking v1)

- Exact trust-gate mechanism for publishing (allowlist identity vs. signed invite token) — decide in Phase 1b.
- Deep-link state storage for Telegram chat→bundle binding (per-chat grain vs. transport-side map) — decide in Phase 1d.
- When to pull the exportable bundle file from Phase 2 to Phase 1.5 (driven by first peer-sharing demand).
- Phase 2 isolation choice (per-tenant brains vs. sandbox) — defer until public publishing is real demand.

---

## 14. Glossary

- **Neuron** — an Orleans grain (`INeuron : IGrainWithStringKey`); an actor.
- **Synapse** — an immutable typed message (`[GenerateSerializer]` record) carrying causal lineage.
- **NeuroPack** — the atomic distributable: name/version/typed-C# code, ECDSA-signed, priced.
- **Bundle** — a `NeuroPack` + manifest (tier, entry experience, config, channels, deps). The product noun.
- **Experience** — a named user journey (ordered hops) declared inside a bundle.
- **Embodiment** — compile (Roslyn) → collectible ALC → live `IPackBehavior` in a `GeneratedNeuron`; the N+1 mechanism.
- **Channel** — a delivery surface (in-app web SDUI, Telegram); bundles target channels, transports adapt output.
- **Substrate / Channel / Content bundle** — the three tiers (§4).
```
