namespace DigitalBrain.Silo;

// Isolation tiers for executing untrusted pack code, weakest to strongest:
//  - InProcessGated: collectible ALC + CapabilityGate (PackAlcEmbodier) — same process, a guardrail not a sandbox.
//  - OutOfProcess:   a separate runtime process — real OS-level isolation (implemented here).
//  - Wasm:           a WASM runtime (Wasmtime). The strongest, but NET-NEW with zero prior art in any tree and no
//                    C#-pack -> wasm toolchain yet; documented as the next hardening tier, not implemented.
public enum SandboxTier
{
    InProcessGated,
    OutOfProcess,
    Wasm
}

public sealed record SandboxResult(bool Success, string Output, string Error);

// Executes untrusted pack code in isolation. The strength of the isolation is the implementation's Tier.
public interface ISandboxedExecutor
{
    SandboxTier Tier { get; }
    Task<SandboxResult> RunAsync(string source, CancellationToken ct = default);
}
