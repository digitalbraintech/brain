# CONTINUITY — NeuroOS best-of-breed consolidation

## Snapshot
[2026-06-24 | provenance: user task + workflow wdz4vohb7]
Goal: port best-of-breed from E:\DigitalBrainTech reference trees (final, IAW, digitalbrain,
v3, v4, self-improving) into this main repo, layer by layer. Typed C# only — **.ino is dead**.
Grounded 9-layer comparison + 13-step plan done (workflow wdz4vohb7).

Decisions (user, 2026-06-24):
- Embodiment: capability object in collectible ALC dispatched via single host GeneratedNeuron (not per-pack grain).
- Pack signing: ECDSA-nistP256 (BCL-only).
- SDK integration neurons: typed grain-method RPC (zero-reflection), not synapses.
- Marketplace economics: real money NOW (Stripe + ECDSA licenses + Google auth, fail-fast secrets).

Keystone (step 4): real InstallFromMarketplace -> CapabilityGate+verify -> Roslyn compile pack.Code
-> collectible ALC load -> dispatch synapse -> assert pack's REAL emission -> Unload. Today install
is a stub (SystemNeurons.cs:58) that LLM-"embodies" instead of compiling.

## Done
- [2026-06-24] Step 1 (L1 Core): stable SynapseId + CausationId (append-only Id 6/7) + lineage-propagating
  Stamp; Neuron tracks _cause across DeliverAsync. Tests green (causation + JSON round-trip + regressions).
- [2026-06-24] Step 2 (L4 SDK pilot): INeuronAgent static-virtual metadata (zero-reflection NeuronAgentMetadata)
  + IGitNeuron/GitNeuron re-homed from IAW onto Neuron, typed RPC, journal-derived metrics. Tests green.
- [2026-06-24] Step 3 (L5 trust): PackSignatureVerifier (ECDSA-nistP256, BCL) + NeuroPack AuthorPublicKey/
  Signature (Id 7/8) + PublishToMarketplace fields; install verifies — invalid rejected, unsigned warn-only
  (RejectUnsignedPacks=false, flip before remote install). Tests green.
- [2026-06-24] Step 4 (L2 KEYSTONE): IPackBehavior contract + PackEmission synapse; PackAlcEmbodier compiles
  pack.Code -> CapabilityGate -> collectible ALC (v3 Resolving hook + SuppressFlow) -> instantiate IPackBehavior;
  FoundryCompilation.CreateWith/TpaReferences (v3). MarketplaceNeuron install delivers the pack to GeneratedNeuron,
  which now EMBODIES real compiled C# and fires PackEmission (LLM only as fallback for non-compilable packs).
  Acceptance test proves install->compile->ALC->dispatch->real emission, e2e, typed-C#. 75/77 green (2 = the
  pre-existing env socket-bind GatewayGrpcWireTests, verified on clean baseline; not regressions).

## Working set (next steps, from plan)
- Step 5: L6 MCP HTTP co-hosted in silo's existing WebApplication (IAW WithHttpTransport+MapMcp; 2nd Kestrel
  endpoint Http1AndHttp2; relocate DigitalBrainTools <300 LOC; delete dead [SIMULATED]/[DEMO] fallbacks).
- Step 6: rest of SDK neurons (Shell/FileSystem/DotNet/NuGet/Roslyn + build IWinget new). Step 7: kernel branch
  dedup (SynapseId now available) + encrypted checkpoint/restore. Step 8: real SE review (final ProjectReview).
  Step 9: Context in-grain hybrid + embeddings (Qdrant later).
- Economics (user: real money NOW): ECDSA LicenseNeuron + Stripe behind IPaymentGateway + Google auth, fail-fast secrets.

## Open questions
- Trust policy unsigned packs: warn-only during transition then strict (default, not yet ratified).
- Substrate for cloud self-update / Key Vault keying: local-Aspire now; ACA later.
- Remote MCP auth scheme before External=true. Canonical SDUI model before L9.
- Commit cadence: not committing until user asks (per CLAUDE.md).
