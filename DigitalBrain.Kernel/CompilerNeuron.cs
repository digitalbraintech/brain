using DigitalBrain.Core;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.compiler.v1")]
public class CompilerNeuron(ILogger<CompilerNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), ICompiler
{
    public async Task HandleAsync(CreateNeuronRequest req)
    {
        Logger.LogInformation("Compiler generating for: {Desc}", req.Description);
        var packName = "Generated" + req.Description.Replace(" ", "").Replace("\"", "").Replace("-", "").Substring(0, Math.Min(18, req.Description.Length));
        string snippet;

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            var sys = "You are expert C# generator for real working software. Output ONLY complete minimal self-contained console app (top level or Main/Run) fulfilling the spec (may be .feature or English desc like 'process last 100 emails on PC, write report.txt with subjects/bodies'). Use file IO for archive. Only stdlib. Respond ONLY ```csharp block. (Neuron style only if requested)";
            var user = $"Description: {req.Description}\nBase name hint: {packName}";
            var fullPrompt = sys + "\n\n" + user;

            var response = await chat.GetResponseAsync(fullPrompt);
            var acc = response.Text;
            snippet = ExtractCode(acc);
            if (string.IsNullOrWhiteSpace(snippet))
                snippet = FallbackGeneralCode(packName, req.Description);
        }
        else
        {
            snippet = FallbackGeneralCode(packName, req.Description);
        }

        await FireAsync(new NeuronCodeGenerated(req.Description, snippet));
        await FireAsync(new NeuronTelemetry(Self, "code-generated"));

        var pack = new NeuroPack(packName, "0.1-dev", "compiler", false, 0.10, snippet, req.Description);
    }

    static string ExtractCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var start = text.IndexOf("```csharp", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += 9;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text.Substring(start, end - start).Trim();
        }
        start = text.IndexOf("```", StringComparison.Ordinal);
        if (start >= 0)
        {
            start += 3;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text.Substring(start, end - start).Trim();
        }
        var c = text.IndexOf("public class ", StringComparison.Ordinal);
        if (c >= 0) return text.Substring(c).Trim();
        return text.Trim();
    }

    static string FallbackGeneralCode(string pack, string desc) =>
        $@"using System;
using System.IO;

public class {pack}
{{
    public static void Run(string input = """")
    {{
        var data = ""Processed data for: {desc}\nInput: "" + input + ""\nResult: report written.\n"";
        File.WriteAllText(""report.txt"", data);
        Console.WriteLine(""Wrote report.txt with processed "" + desc);
    }}
    public static void Main(string[] args) => Run(args.Length > 0 ? string.Join("" "", args) : """");
}}";
}


