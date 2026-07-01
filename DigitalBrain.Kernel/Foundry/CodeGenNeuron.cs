using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Foundry;

[GrainType("digitalbrain.codegen.v1")]
public class CodeGenNeuron(ILogger<CodeGenNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICodeGenNeuron
{
    public async Task HandleAsync(GenerateCode cmd)
    {
        var source = await GenerateSourceAsync(cmd);
        var refs = new[] { "System.Runtime", "DigitalBrain.Core" };
        await FireAsync(new CodeGenerated(cmd.Spec, source, cmd.Tier, refs));
    }

    private async Task<string> GenerateSourceAsync(GenerateCode cmd)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
            return FallbackSource(cmd);

        var system = cmd.Tier == TargetTier.Run
            ? "You generate ONE self-contained C# class with: public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input). No I/O outside given input. Respond ONLY with a ```csharp block."
            : "You generate ONE Orleans grain neuron deriving from DigitalBrain.Kernel.Neuron with [GrainType] and an IHandle<T> handler. Respond ONLY with a ```csharp block.";
        var prompt = system + "\n\nSpec: " + cmd.Spec + "\nHints: " + cmd.Hints;

        var response = await chat.GetResponseAsync(prompt);
        var extracted = ExtractCode(response.Text);
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

