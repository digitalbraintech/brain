using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Silo.Foundry;

public static class FoundryCompilation
{
    public static CSharpCompilation Create(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release);
        return CSharpCompilation.Create(assemblyName, new[] { tree }, references, options);
    }

    // Compile against the trusted-platform-assemblies set plus explicit extra assemblies (e.g. DigitalBrain.Protocol
    // so a pack can implement IPackBehavior). Cleaner + deterministic vs. scanning the runtime dir — harvested from
    // v3's InoCompiler reference resolution.
    public static CSharpCompilation CreateWith(string assemblyName, string source, params Assembly[] extraAssemblies)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release);
        return CSharpCompilation.Create(assemblyName, new[] { tree }, TpaReferences(extraAssemblies), options);
    }

    public static IReadOnlyList<MetadataReference> TpaReferences(params Assembly[] extraAssemblies)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
        }
        foreach (var assembly in extraAssemblies)
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                paths.Add(assembly.Location);

        return paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
    }

    public static IReadOnlyList<MetadataReference> DefaultReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new List<MetadataReference>();
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            try
            {
                // GetAssemblyName throws BadImageFormatException for native DLLs; use it as a managed-PE filter
                System.Reflection.AssemblyName.GetAssemblyName(dll);
                refs.Add(MetadataReference.CreateFromFile(dll));
            }
            catch { /* skip native or unreadable dlls */ }
        }
        return refs;
    }
}
