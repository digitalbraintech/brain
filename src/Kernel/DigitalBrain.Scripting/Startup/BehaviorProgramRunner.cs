using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Signals;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.Scripting.Startup;
internal sealed class BehaviorProgramRunner
{
    private readonly ConcurrentDictionary<(Guid Revision, string SourceHash, string Runtime), Script<object>> _compiled = new();
    public static string RuntimeFingerprint { get; } = RuntimeIdentity();

    private static string RuntimeIdentity()
    {
        // Fingerprint the compiler and its exact references, independent of which
        // unrelated modules happened to activate before this executor started.
        var assemblies = CSharpStartupScriptRunner.Options.MetadataReferences.OfType<PortableExecutableReference>()
            .Select(reference => reference.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!)
            .Append(typeof(BehaviorProgramRunner).Assembly.Location).Append(typeof(CSharpScript).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(path => $"{Path.GetFileName(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.Version + "\n" + string.Join("\n", assemblies))));
    }
    public string[] Validate(BehaviorProgram program)
    {
        if (Regex.IsMatch(program.Source, "__[A-Z][A-Z0-9_]+__", RegexOptions.CultureInvariant))
        {
            return["This draft contains unresolved setup placeholders. Resolve the source and configuration before activation."];
        }

        var script = Compile(program);
        return script.Compile().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.ToString()).ToArray();
    }

    public (string[] Input, string[] Output) DeclaredTypes(BehaviorProgram program)
    {
        var compilation = Compile(program).GetCompilation();
        var tree = compilation.SyntaxTrees.Last();
        var syntax = tree.GetRoot();
        var semantics = compilation.GetSemanticModel(tree);
        static bool IsInput(ExpressionSyntax expression) => expression.ToString().TrimEnd('!') is "Input" or "Signal";
        static bool IsSignal(ITypeSymbol? type)
        {
            for (var candidate = type as INamedTypeSymbol; candidate is not null; candidate = candidate.BaseType)
            {
                if (candidate.ToDisplayString() == "DigitalBrain.Abstractions.Signals.Signal")
                {
                    return true;
                }
            }

            return false;
        }

        var inferredInputTypes = syntax.DescendantNodes().OfType<GenericNameSyntax>()
            .Where(node => node.Identifier.ValueText == "Input" && node.TypeArgumentList.Arguments.Count == 1)
            .Select(node => node.TypeArgumentList.Arguments[0])
            .Concat(syntax.DescendantNodes().OfType<DeclarationPatternSyntax>()
                .Where(node => node.Ancestors().OfType<IsPatternExpressionSyntax>().FirstOrDefault() is { } pattern && IsInput(pattern.Expression))
                .Select(node => node.Type))
            .Concat(syntax.DescendantNodes().OfType<CastExpressionSyntax>().Where(node => IsInput(node.Expression)).Select(node => node.Type));
        var inputs = program.InputSignalTypes.Length > 0 ? program.InputSignalTypes : inferredInputTypes
            .Select(node => semantics.GetTypeInfo(node).Type).Where(IsSignal).Select(type => type!.Name).Distinct(StringComparer.Ordinal).ToArray();
        var returns = syntax.DescendantNodes().OfType<ReturnStatementSyntax>().Where(node => node.Expression is not null).Select(node => node.Expression!).ToArray();
        var publishOutputs = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(node => node.Expression.ToString().Contains("PublishAsync", StringComparison.Ordinal))
            .SelectMany(node => node.ArgumentList.Arguments)
            .Select(argument => argument.Expression)
            .OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => semantics.GetTypeInfo(creation).Type);
        var outputs = program.OutputSignalTypes.Length > 0 ? program.OutputSignalTypes : returns
            .Select(expression => semantics.GetTypeInfo(expression).Type).Where(IsSignal)
            .Where(type => type!.Name != nameof(Signal)).Select(type => type!.Name)
            .Concat(publishOutputs.Where(IsSignal).Select(type => type!.Name))
            .Concat(returns.Any(IsInput) ? inputs : []).Distinct(StringComparer.Ordinal).ToArray();
        return (inputs, outputs);
    }

    public async Task Run(BehaviorProgram program, IDigitalBrain brain, Signal input, string name, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(program.Source)));
        using var connection = DigitalBrainClient.BindExecution(brain);
        await Compile(program).RunAsync(new StartupScriptContext(brain, cancellationToken, new(name, program.Revision, hash), input), cancellationToken).ConfigureAwait(false);
    }

    private Script<object> Compile(BehaviorProgram program) => _compiled.GetOrAdd((program.Revision,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(program.Source))), RuntimeFingerprint), _ =>
    {
        // Roslyn script globals are fields, which cannot contain ordinary C# using
        // declarations. A generated async method preserves normal C# local lifetime
        // and disposal without changing the saved, editable source.
        var syntax = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(program.Source,
            new CSharpParseOptions(LanguageVersion.Preview, kind: SourceCodeKind.Regular)).GetRoot();
        var body = program.Source.ToCharArray();
        foreach (var directive in syntax.Usings)
        {
            for (var index = directive.SpanStart; index < directive.Span.End; index++)
            {
                if (body[index] is not ('\r' or '\n'))
                {
                    body[index] = ' ';
                }
            }
        }

        var entry = $"__DigitalBrainBehavior_{program.Revision:N}";
        var source = string.Join(Environment.NewLine, syntax.Usings.Select(directive => directive.ToString()))
            + $"\nasync System.Threading.Tasks.Task {entry}()\n{{\n#line 1 \"behavior.csx\"\n"
            + new string(body) + $"\n#line default\n}}\nawait {entry}();\nreturn 0;";
        return CSharpScript.Create<object>(source, CSharpStartupScriptRunner.Options, typeof(StartupScriptContext));
    });
}
