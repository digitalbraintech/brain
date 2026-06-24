# CONTINUITY — NeuroOS best-of-breed consolidation

## Snapshot
[2026-06-24 | provenance: user task + workflow wdz4vohb7]
Port best-of-breed from E:\DigitalBrainTech reference trees (final, IAW, digitalbrain, v3, v4) into this main
repo, layer by layer. Typed C# only — **.ino is dead**. 13-step plan from workflow wdz4vohb7.
Branch: **consolidation/best-of-breed**, commit per step, test after each.

User decisions (2026-06-24):
- Embodiment = capability object in collectible ALC via single host GeneratedNeuron (not per-pack grain).
- Pack signing = ECDSA-nistP256 (BCL). SDK integration neurons = typed grain RPC.
- Marketplace economics = real money NOW (Stripe + ECDSA licenses + Google auth, fail-fast secrets).
- MCP internal-only (no External ingress). Commit to a separate branch.

Test baseline: 2 GatewayGrpcWireTests fail on a pre-existing env socket-bind (verified on clean baseline) —
NOT regressions; everything else green.

## Done (commit per step)
- Steps 1-3: L1 causation (stable SynapseId + CausationId Id6/7 + lineage Stamp); L4 SDK pilot (INeuronAgent
  static-virtual metadata + IGitNeuron from IAW, typed RPC); L5 trust (PackSignatureVerifier ECDSA-nistP256 +
  NeuroPack pubkey/sig Id7/8; install rejects invalid sig, unsigned warn-only).
- Step 4 (KEYSTONE): IPackBehavior + PackEmission; PackAlcEmbodier compiles pack.Code -> CapabilityGate ->
  collectible ALC (v3 Resolving hook + SuppressFlow + TpaReferences) -> IPackBehavior; install delivers pack to
  GeneratedNeuron which runs REAL compiled C# + fires PackEmission (LLM fallback only). e2e tested.
- Step 5 (MCP): DigitalBrainTools -> shared DigitalBrain.Mcp.Tools (3 partials, IGrainFactory ctor, fabricating
  fallbacks deleted). Silo co-hosts MCP/HTTP on Kestrel 8081 Http1AndHttp2, MapMcp().RequireHost("*:8081"),
  in-process, internal-only. Shared NeuronTestSiloConfigurator.
- Step 6 (SDK rest): shared ProcessRunner + typed Shell/FileSystem/DotNet/NuGet/Winget(net-new)/Roslyn neurons;
  GitNeuron onto ProcessRunner; retired untyped NuGet/Roslyn neurons.
- Step 7 (Kernel): checkpoint dedup by SynapseId; BranchAsync forks into SAME grain type; RestoreCheckpointAsync;
  INeuronStateProtector + AES-GCM/PassThrough + CheckpointProtector + AddKernelSecurity DI. 86/88 green.
- Step 8 (Awesome): ProjectReview.Analyze ported near-verbatim from final + ReviewRequest/ReviewProjectRequest/
  ReviewResult + SoftwareEngineeringReviewerNeuron (real review, not string templating). 91/93 green.
- Step 9 (Context): HybridScorer (cosine + keyword, zero-vector fallback, from IAW) + NoOpEmbeddingGenerator
  (registered fail-soft in DigitalBrainChat) + ContextNeuron RememberAsync/RecallAsync + MemoryStored synapse.
  In-grain hybrid memory works with zero deps (keyword when NoOp). 95/97 green.

## Working set (next)
- Economics (user: real money NOW): IPaymentGateway (synthetic + Stripe) + ECDSA LicenseNeuron (issue/verify,
  reuse PackSignatureVerifier) + premium-gated install. Google auth optional. Fail-fast secrets.
- Deferred (plan): L9 UI streaming pipeline + Flutter client; WASM sandbox; external Qdrant + PDF ingestion (real embeddings).

## Open questions
- Encryption keying for cloud (DPAPI/local vs Key Vault) — AES key via DigitalBrain:Checkpoint:Key; Key Vault wiring TBD.
- Trust policy: flip RejectUnsignedPacks=true before any remote/untrusted install. MCP External+auth deferred.
