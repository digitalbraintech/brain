using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Time;
using DigitalBrain.UI;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.Scripting.Startup;

internal sealed class CSharpStartupScriptRunner : IStartupScriptRunner
{
    internal static readonly ScriptOptions Options = ScriptOptions.Default
        .WithReferences(
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(File).Assembly,
            typeof(Process).Assembly,
            typeof(SHA256).Assembly,
            typeof(JsonSerializer).Assembly,
            typeof(HttpClient).Assembly,
            typeof(IDigitalBrain).Assembly,
            typeof(DigitalBrainClient).Assembly,
            typeof(IAssistant).Assembly,
            typeof(DigitalBrain.Microsoft.IAspire).Assembly,
            typeof(DigitalBrain.Google.IGmail).Assembly,
            typeof(DigitalBrain.Salesforce.ISalesforce).Assembly,
            typeof(DigitalBrain.Time.ITimer).Assembly,
            typeof(DigitalBrain.Product.Identity.CommandId).Assembly,
            typeof(IChart).Assembly)
        .WithImports(
            "System",
            "System.Collections.Generic",
            "System.Diagnostics",
            "System.IO",
            "System.Linq",
            "System.Security.Cryptography",
            "System.Text",
            "System.Threading",
            "System.Threading.Tasks",
            "DigitalBrain.Abstractions",
            "DigitalBrain.Abstractions.Identity",
            "DigitalBrain.Abstractions.Journals",
            "DigitalBrain.Abstractions.Neurons",
            "DigitalBrain.Abstractions.Signals",
            "DigitalBrain.Sdk",
            "DigitalBrain.Sdk.Webhooks",
            "DigitalBrain.AI",
            "DigitalBrain.Microsoft",
            "DigitalBrain.Microsoft.GitHub",
            "DigitalBrain.Google",
            "DigitalBrain.Salesforce",
            "DigitalBrain.Chat",
            "DigitalBrain.Time",
            "DigitalBrain.Product.Identity",
            "DigitalBrain.UI");

    public async Task<StartupScriptRunResult> RunAsync(
        StartupScript script,
        IDigitalBrain brain,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await CSharpScript.RunAsync(
                script.Source,
                Options,
                new StartupScriptContext(brain, cancellationToken, script.Behavior),
                typeof(StartupScriptContext),
                cancellationToken);

            return new StartupScriptRunResult(
                true,
                state.ReturnValue?.ToString() ?? "completed",
                []);
        }
        catch (CompilationErrorException exception)
        {
            return new StartupScriptRunResult(
                false,
                "Compilation failed.",
                exception.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new StartupScriptRunResult(false, exception.Message, []);
        }
    }
}
