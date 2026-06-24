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
  + ContextNeuron RememberAsync/RecallAsync + MemoryStored synapse. Zero-dep hybrid memory. 95/97 green.
- Economics (user: real money NOW): ECDSA LicenseNeuron (issue/verify/entitlement, reuses PackSignatureVerifier,
  journal-persistent keypair) + NeuroPack.Price (Id9) + premium-gated install + IPaymentGateway with
  SyntheticPaymentGateway (tested) and StripePaymentGateway (real Stripe.net 48.1.0, behind config, fail-fast).
  99/101 green.

## Extended (deferred items, user-requested)
- L9 UI backbone DONE: RfwCard synapse + HomeFeedBus (fanout+dedup) + ChatNeuron (IHandle<VisualizeDataRequest> -> RfwCard) + streaming gRPC WatchHomeFeed. SDUI: UiSurface canonical, RfwCard added. Flutter client + gRPC wire test deferred (env sockets).
- Context phase 2 DONE: IVectorStore + InMemoryVectorStore (tested) + QdrantVectorStore (build-verified) + TextChunker + DocumentIngestor. PDF deferred (feed lacks stable PdfPig); real embeddings are a drop-in IEmbeddingGenerator swap.
- Sandbox DONE: ISandboxedExecutor + OutOfProcessSandbox (child-process isolation, CapabilityGate-screened, tested). True WASM (Wasmtime) documented as the next tier, not built.
- ALL deferred items addressed. Only the 108-file Flutter CLIENT + live Qdrant/Ollama/Stripe/gRPC-wire need external infra (env-blocked here).
## Remaining:
- L9 UI: server-side streaming gRPC pipeline (uigateway.proto + HomeFeedBus + ConversationGrain + RfwCard) and
  the 108-file Flutter client. XL; needs a canonical-SDUI-model decision (keep UiSurface + add RfwCard, vs migrate).
- WASM/IWasm sandbox: net-new, zero prior art; only if untrusted third-party packs must run sandboxed.
- Context phase 2: external Qdrant container + real embeddings (Ollama/OpenAI) + PdfIngestionSource/DocumentIngestor.
- Google auth for marketplace (optional, pairs with economics).

## Open questions
- Encryption keying for cloud (DPAPI/local vs Key Vault) — AES key via DigitalBrain:Checkpoint:Key; Key Vault wiring TBD.
- Trust policy: flip RejectUnsignedPacks=true before any remote/untrusted install. MCP External+auth deferred.
