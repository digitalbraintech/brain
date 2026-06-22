using System.IO;
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

    public static IReadOnlyList<MetadataReference> DefaultReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new List<MetadataReference>();
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            try { refs.Add(MetadataReference.CreateFromFile(dll)); }
            catch { /* skip non-managed or unreadable dlls */ }
        }
        return refs;
    }
}
