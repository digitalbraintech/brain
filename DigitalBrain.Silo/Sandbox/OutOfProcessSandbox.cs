using DigitalBrain.Silo.Foundry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace DigitalBrain.Silo;

// Runs untrusted pack code in a SEPARATE runtime process — the isolation boundary the in-process collectible-ALC
// guardrail (PackAlcEmbodier) cannot provide (.NET has no in-process CAS). The pack is a console program: it is
// CapabilityGate-screened, compiled to a temp executable, and launched via `dotnet` as a child process with a
// timeout; its stdout/stderr are captured. This is the realistic security sandbox the Foundry README names as the
// hardening over the in-process executor; a WASM tier (Wasmtime) is the stronger, not-yet-built next step.
public sealed class OutOfProcessSandbox : ISandboxedExecutor
{
    public SandboxTier Tier => SandboxTier.OutOfProcess;

    public async Task<SandboxResult> RunAsync(string source, CancellationToken ct = default)
    {
        var assemblyName = "sandbox_" + Guid.NewGuid().ToString("N");
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            FoundryCompilation.TpaReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release));

        var violations = CapabilityGate.FindViolations(compilation);
        if (violations.Count > 0)
            return new SandboxResult(false, "", "capability gate rejected: " + string.Join(", ", violations));

        var tempDirectory = Path.Combine(Path.GetTempPath(), assemblyName);
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var dllPath = Path.Combine(tempDirectory, assemblyName + ".dll");

            EmitResult emit;
            using (var stream = new FileStream(dllPath, FileMode.Create))
                emit = compilation.Emit(stream);

            if (!emit.Success)
            {
                var errors = emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString());
                return new SandboxResult(false, "", "compile error: " + string.Join("; ", errors));
            }

            CopyHostRuntimeConfig(dllPath);

            var result = await ProcessRunner.RunAsync("dotnet", $"\"{dllPath}\"", tempDirectory, timeoutMs: 30_000, ct: ct);
            return new SandboxResult(result.Succeeded, result.Output, result.Error);
        }
        finally
        {
            TryDelete(tempDirectory);
        }
    }

    // Reuse the host's runtimeconfig so the child process binds the same installed shared framework.
    private static void CopyHostRuntimeConfig(string dllPath)
    {
        var hostConfig = Directory.GetFiles(AppContext.BaseDirectory, "*.runtimeconfig.json").FirstOrDefault();
        if (hostConfig is not null)
            File.Copy(hostConfig, Path.ChangeExtension(dllPath, ".runtimeconfig.json"), overwrite: true);
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }
}
