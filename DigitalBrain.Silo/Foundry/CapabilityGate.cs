using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.Silo.Foundry;

public static class CapabilityGate
{
    private static readonly string[] BannedNamespacePrefixes =
    {
        "System.Diagnostics.Process.",
        "System.Reflection.Emit.",
        "System.Runtime.InteropServices.",
        "System.Runtime.Loader.",
        "Microsoft.Win32.Registry."
    };

    public static IReadOnlyList<string> FindViolations(CSharpCompilation compilation)
    {
        var violations = new HashSet<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodes())
            {
                if (node is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax))
                    continue;

                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol is null)
                    continue;

                var fullName = symbol.ContainingType is null
                    ? symbol.ToDisplayString()
                    : symbol.ContainingType.ToDisplayString() + "." + symbol.Name;

                foreach (var banned in BannedNamespacePrefixes)
                {
                    if (fullName.StartsWith(banned, StringComparison.Ordinal))
                        violations.Add(banned);
                }
            }
        }
        return violations.ToList();
    }
}
