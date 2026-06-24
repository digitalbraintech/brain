using System.Reflection;
using System.Runtime.Loader;
using DigitalBrain.Protocol;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Silo.Foundry;

// THE KEYSTONE embodiment engine: compile a typed-C# pack -> CapabilityGate -> collectible ALC -> instantiate
// its IPackBehavior, returning a live, disposable capability the host GeneratedNeuron dispatches to.
// Harvests v3 GateNeuron's Resolving host-dependency hook (so the pack's reference to DigitalBrain.Protocol
// unifies with the running host, making the IPackBehavior cast valid) and SuppressFlow-for-collectibility,
// plus v3 InoCompiler's TPA reference resolution. No .ino: the pack IS C#.
public interface IPackEmbodiment
{
    EmbodiedPack Embody(string packName, string source);
}

public sealed class EmbodiedPack(string packName, AssemblyLoadContext context, IPackBehavior behavior) : IDisposable
{
    public string PackName { get; } = packName;

    public string Respond(string input) => behavior.Respond(input);

    // Dropping the only reference + Unload lets the collectible ALC be GC'd (e.g. on uninstall / new version).
    public void Dispose() => context.Unload();
}

public sealed class PackAlcEmbodier : IPackEmbodiment
{
    public EmbodiedPack Embody(string packName, string source)
    {
        var assemblyName = "pack_" + Sanitize(packName) + "_" + Guid.NewGuid().ToString("N");
        var compilation = FoundryCompilation.CreateWith(assemblyName, source, typeof(IPackBehavior).Assembly);

        var violations = CapabilityGate.FindViolations(compilation);
        if (violations.Count > 0)
            throw new PackEmbodimentException($"capability gate rejected pack '{packName}': {string.Join(", ", violations)}");

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        if (!emit.Success)
        {
            var errors = emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            throw new PackEmbodimentException($"pack '{packName}' failed to compile: {string.Join("; ", errors)}");
        }
        peStream.Seek(0, SeekOrigin.Begin);

        // SuppressFlow so ambient async-locals do not pin the ALC; it stays collectible after Unload.
        using (ExecutionContext.SuppressFlow())
        {
            var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
            context.Resolving += ResolveFromHost;
            try
            {
                var assembly = context.LoadFromStream(peStream);
                var behavior = Instantiate(assembly)
                    ?? throw new PackEmbodimentException($"pack '{packName}' has no public parameterless IPackBehavior implementation");
                return new EmbodiedPack(packName, context, behavior);
            }
            catch
            {
                context.Unload();
                throw;
            }
        }
    }

    private static IPackBehavior? Instantiate(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is { IsClass: true, IsAbstract: false }
                && typeof(IPackBehavior).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                return (IPackBehavior?)Activator.CreateInstance(type);
            }
        }
        return null;
    }

    // Unify shared assemblies (DigitalBrain.Protocol et al.) with the host so the loaded type's IPackBehavior
    // IS the host's IPackBehavior — only then is the cast in Instantiate valid across the ALC boundary.
    private static Assembly? ResolveFromHost(AssemblyLoadContext context, AssemblyName name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.Ordinal));

    private static string Sanitize(string name) =>
        new(name.Where(char.IsLetterOrDigit).ToArray());
}

public sealed class PackEmbodimentException(string message) : Exception(message);
