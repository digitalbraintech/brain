using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace DigitalBrain.Silo.Foundry;

public sealed class InProcessAlcExecutor : ICodeExecutor
{
    public CodeExecutionResult Execute(string source, string entrypoint)
    {
        var references = FoundryCompilation.DefaultReferences();
        var compilation = FoundryCompilation.Create("foundry_gen_" + Guid.NewGuid().ToString("N"), source, references);

        var violations = CapabilityGate.FindViolations(compilation);
        if (violations.Count > 0)
            return new CodeExecutionResult(false, "", "capability gate rejected: " + string.Join(", ", violations));

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        if (!emit.Success)
        {
            var errors = emit.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            return new CodeExecutionResult(false, "", "compile error: " + string.Join("; ", errors));
        }

        peStream.Seek(0, SeekOrigin.Begin);
        var context = new AssemblyLoadContext("foundry-run", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(peStream);
            var method = FindEntrypoint(assembly, entrypoint);
            if (method is null)
                return new CodeExecutionResult(false, "", $"entrypoint '{entrypoint}' not found");

            var captured = new StringBuilder();
            var originalOut = Console.Out;
            using var writer = new StringWriter(captured);
            Console.SetOut(writer);
            try
            {
                var returned = method.Invoke(null, new object?[] { new Dictionary<string, object?>() });
                var output = captured.ToString();
                if (returned is not null)
                    output += returned;
                return new CodeExecutionResult(true, output, "");
            }
            catch (TargetInvocationException tie)
            {
                return new CodeExecutionResult(false, captured.ToString(), "runtime error: " + (tie.InnerException?.Message ?? tie.Message));
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    private static MethodInfo? FindEntrypoint(Assembly assembly, string entrypoint)
    {
        foreach (var type in assembly.GetTypes())
        {
            var method = type.GetMethod(entrypoint, BindingFlags.Public | BindingFlags.Static);
            if (method is not null)
                return method;
        }
        return null;
    }
}
