using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace DigitalBrain.Silo.Foundry;

[GrainType("digitalbrain.codegen.v1")]
public class CodeGenNeuron : Neuron, ICodeGenNeuron
{
    public CodeGenNeuron(ILogger<CodeGenNeuron> logger) : base(logger) { }

    public async Task HandleAsync(GenerateCode cmd)
    {
        var source = await GenerateSourceAsync(cmd);
        var refs = new[] { "System.Runtime", "DigitalBrain.Protocol" };
        await FireAsync(new CodeGenerated(cmd.Spec, source, cmd.Tier, refs));
    }

    private async Task<string> GenerateSourceAsync(GenerateCode cmd)
    {
        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm is null)
            return FallbackSource(cmd);

        var system = cmd.Tier == TargetTier.Run
            ? "You generate ONE self-contained C# class with: public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input). No I/O outside given input. Respond ONLY with a ```csharp block."
            : "You generate ONE Orleans grain neuron deriving from DigitalBrain.Silo.Neuron with [GrainType] and an IHandle<T> handler. Respond ONLY with a ```csharp block.";
        var prompt = system + "\n\nSpec: " + cmd.Spec + "\nHints: " + cmd.Hints;

        llm.SelectedModel = "qwen2.5-coder:1.5b";
        var accumulated = "";
        await foreach (var chunk in llm.GenerateAsync(prompt))
            if (chunk?.Response is string t) accumulated += t;

        var extracted = ExtractCode(accumulated);
        return string.IsNullOrWhiteSpace(extracted) ? FallbackSource(cmd) : extracted;
    }

    private static string FallbackSource(GenerateCode cmd) => cmd.Tier == TargetTier.Run
        ? "public static class Module { public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input) => \"fallback: " + cmd.Spec.Replace("\"", "'") + "\"; }"
        : "// fallback deploy module for: " + cmd.Spec;

    private static string ExtractCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var start = text.IndexOf("```csharp", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += 9;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text.Substring(start, end - start).Trim();
        }
        return text.Trim();
    }
}
