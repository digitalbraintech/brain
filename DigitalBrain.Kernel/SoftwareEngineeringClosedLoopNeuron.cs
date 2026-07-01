using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
namespace DigitalBrain.Kernel;

public class SoftwareEngineeringClosedLoopNeuron : Neuron, IClosedLoopNeuron
{
    private McpClient? _aspireMcp;

    public SoftwareEngineeringClosedLoopNeuron(ILogger<SoftwareEngineeringClosedLoopNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(ClosedLoopRequest req)
    {
        Logger.LogInformation("ClosedLoop {Type} requested: {Prompt}", req.LoopType, req.Prompt);

        var chat = ServiceProvider.GetService<IChatClient>();
        string analysis = "no-llm-fallback";

        if (chat != null)
        {
            string sysPrompt;
            if (req.LoopType.Equals("ui", StringComparison.OrdinalIgnoreCase) || req.LoopType.Contains("dart", StringComparison.OrdinalIgnoreCase))
            {
                sysPrompt = "You are the UI Closed Loop. Use Dart MCP tools (connect_dart_tooling_daemon with DTD uri, get_widget_tree summaryOnly:true for user code, get_selected_widget, get_runtime_errors, hot_reload, launch_app on sdk/flutter_demo) to inspect live Flutter widget trees while authoring. Propose precise Dart code changes to improve InoCodeEditor, surfaces, skill integration in the workbench. Output: tree summary, proposed file edits or new widget code, then hot reload command.";
            }
            else
            {
                sysPrompt = "You are the SoftwareEngineering ClosedLoopNeuron. Inspect via Aspire MCP (list_resources, list_structured_logs, list_traces), use local context from journals. Propose runtime modifications to neurons/marketplace/INO/editor. Apply via marketplace publish+install for new behavior, or Aspire execute_resource_command restart on resources (silo etc) because multiple kernels may run. Prefer safe Aspire-orchestrated applies + checkpoints. Be concise.";
            }
            var full = sysPrompt + "\nPROMPT: " + req.Prompt + "\nCTX: journal-driven";
            try
            {
                var response = await chat.GetResponseAsync(full);
                var acc = response.Text;
                analysis = string.IsNullOrWhiteSpace(acc) ? "processed" : acc.Trim();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "ClosedLoop LLM analysis failed; recording fallback completion.");
                analysis = "llm-error-fallback: " + ex.GetBaseException().Message;
            }
        }

        await FireAsync(new ClosedLoopCompleted(req.LoopType, analysis.Length > 20 ? analysis : "processed", false));

        var shouldAttemptAspireApply =
            !req.LoopType.Contains("ui", StringComparison.OrdinalIgnoreCase) &&
            (analysis.Contains("restart", StringComparison.OrdinalIgnoreCase) ||
             analysis.Contains("apply", StringComparison.OrdinalIgnoreCase));

        if (shouldAttemptAspireApply)
        {
            await EnsureAspireMcpAsync();
            if (_aspireMcp != null)
            {
                try
                {
                    var res = await CallAspireMcpAsync("list_resources");
                    await FireAsync(new SystemModificationProposed("aspire", "closedloop", analysis, "aspire-mcp"));
                    Logger.LogInformation("ClosedLoop would apply via Aspire MCP on resources: {Res}", res.Substring(0, Math.Min(200, res.Length)));
                }
                catch { }
            }
        }
    }

    public async Task HandleAsync(ExperienceUsed used)
    {
        if (used.Pack.Contains("ClosedLoop", StringComparison.OrdinalIgnoreCase) || used.Pack.Contains("UIClosed", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation("ClosedLoop embodied from pack {Pack}", used.Pack);
            await FireAsync(new ClosedLoopRequest(used.Pack.Contains("UI") ? "ui" : "se", "Embodied pack activation: begin closed improvement loop"));
        }
    }

    private async Task EnsureAspireMcpAsync()
    {
        if (_aspireMcp != null) return;
        try
        {
            var workDir = Directory.GetCurrentDirectory();
            _aspireMcp = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire-closedloop",
                    Command = "aspire",
                    Arguments = ["agent", "mcp"],
                    WorkingDirectory = workDir
                }));
            await FireAsync(new SystemStatusChanged("closedloop-aspire-mcp", "connected"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ClosedLoop Aspire MCP connect failed");
        }
    }

    private async Task<string> CallAspireMcpAsync(string tool, object? args = null)
    {
        if (_aspireMcp == null) return "mcp-unavailable";
        var dict = new Dictionary<string, object?>();
        if (args != null)
        {
            foreach (var p in args.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                dict[p.Name] = p.GetValue(args);
        }
        var res = await _aspireMcp.CallToolAsync(tool, dict);
        return res.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no-data";
    }
}


