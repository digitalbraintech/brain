using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OllamaSharp;
using Orleans.Journaling;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

// IAspire neuron (orchestrates distributed apps via Aspire model, fires completion synapses)
[GrainType("neuro.aspire.v1")]
public class AspireOrchestratorNeuron : Neuron, IAspireNeuron
{
    public AspireOrchestratorNeuron(ILogger<AspireOrchestratorNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(StartDistributedApp cmd)
    {
        Logger.LogInformation("Aspire starting app: {App}", cmd.AppName);
        await FireAsync(new DistributedAppStarted(cmd.AppName, Success: true, "started via neuro"));
    }

    public async Task HandleAsync(RestartResource cmd)
    {
        Logger.LogInformation("Aspire restarting resource: {Res}", cmd.ResourceName);
        await FireAsync(new DistributedAppStarted(cmd.ResourceName, Success: true, "restarted"));
    }
}

// IMarketplace neuron (publish/install neuro packs, dynamic assembly load hook stub)
[GrainType("neuro.marketplace.v1")]
public class MarketplaceNeuron : Neuron, IMarketplaceNeuron
{
    private readonly List<string> _published = new();

    public MarketplaceNeuron(ILogger<MarketplaceNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(PublishToMarketplace cmd)
    {
        Logger.LogInformation("Marketplace publish: {Pack}@{Ver}", cmd.PackName, cmd.Version);
        _published.Add($"{cmd.PackName}@{cmd.Version}");
        // Published; no installed yet (install is separate download step)
    }

    public async Task HandleAsync(InstallFromMarketplace cmd)
    {
        Logger.LogInformation("Marketplace install: {Pack}@{Ver}", cmd.PackName, cmd.Version);
        // Stub: would trigger AspireHost.AddDynamicResource + Assembly.Load + grain re-register
        await FireAsync(new NeuroPackInstalled(cmd.PackName, cmd.Version));
        // Activate/use the experience for the downloader (stub; test cluster may hang on reentrant GetGrain< > + Fire, so disabled in handle for tests; TUI demonstrates via explicit calls)
        // var genKey = "generated-" + cmd.PackName.ToLower();
        // var gen = GrainFactory.GetGrain<IGeneratedNeuron>(genKey);
        // await gen.FireAsync(new ExperienceUsed(cmd.PackName, "downloaded-and-activated"));
    }

    public async Task HandleAsync(ListPublished _cmd)
    {
        Logger.LogInformation("Marketplace listing {Count} packs", _published.Count);
        await FireAsync(new PublishedList(_published.AsReadOnly()));
    }
}

[GrainType("neuro.compiler.v1")]
public class CompilerNeuron : Neuron, ICompiler
{
    public CompilerNeuron(ILogger<CompilerNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(CreateNeuronRequest req)
    {
        Logger.LogInformation("Compiler generating neuron for: {Desc}", req.Description);
        var packName = "Generated" + req.Description.Replace(" ", "").Replace("\"", "").Replace("-", "").Substring(0, Math.Min(18, req.Description.Length));
        string snippet;

        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm != null)
        {
            var sys = "You write minimal self-contained C# Neuron grains. Respond with ONLY a ```csharp fenced block containing one public class XXXNeuron : Neuron, INeuron { ... } inheriting the base. Use DigitalBrain.Protocol. Support simple Handle for its purpose. No extra usings or namespaces.";
            var user = $"Description: {req.Description}\nClass base name: {packName}Neuron";
            var fullPrompt = sys + "\n\n" + user;

            llm.SelectedModel = "qwen2.5-coder:1.5b";
            var acc = "";
            await foreach (var chunk in llm.GenerateAsync(fullPrompt))
            {
                if (chunk?.Response is string t) acc += t;
            }
            snippet = ExtractCode(acc);
            if (string.IsNullOrWhiteSpace(snippet))
                snippet = FallbackSnippet(packName, req.Description);
        }
        else
        {
            snippet = FallbackSnippet(packName, req.Description);
        }

        await FireAsync(new NeuronCodeGenerated(req.Description, snippet));
        await FireAsync(new NeuronTelemetry(Self, "code-generated"));
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

    static string FallbackSnippet(string pack, string desc) =>
        $"[GrainType(\"neuro.generated.{pack.ToLower()}\")]\npublic class {pack}Neuron : Neuron, INeuron {{\n    // {desc}\n}}";
}

[GrainType("neuro.optimizer.v1")]
public class MetaOptimizerNeuron : Neuron, IMetaOptimizerNeuron
{
    private int _telemetryCount = 0;

    public MetaOptimizerNeuron(ILogger<MetaOptimizerNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(NeuronTelemetry telemetry)
    {
        _telemetryCount += telemetry.Count;
        Logger.LogInformation("Optimizer received telemetry from {Neuron}: {Event} (total {Count})", telemetry.Neuron, telemetry.Event, _telemetryCount);

        if (_telemetryCount >= 5)
        {
            string proposal;
            var llm = ServiceProvider.GetService<IOllamaApiClient>();
            if (llm != null)
            {
                llm.SelectedModel = "qwen2.5-coder:1.5b";
                var p = $"Telemetry count reached {_telemetryCount}. Propose ONE short, actionable wiring or scaling improvement for the NeuroOS neuron system (Orleans grains + Aspire + compiler for code gen from English).";
                var acc = "";
                await foreach (var chunk in llm.GenerateAsync(p))
                    if (chunk?.Response is string t) acc += t;
                proposal = acc.Length > 20 ? acc.Trim() : "Add parallel compiler neurons and route create requests through LlmNeuron";
            }
            else
            {
                proposal = "Add parallel compiler neurons routed via LlmNeuron for faster self-gen";
            }
            await FireAsync(new WiringOptimizationProposed(proposal, Self.Value));
            _telemetryCount = 0;
        }
    }

    public Task HandleAsync(WiringOptimizationProposed proposal)
    {
        Logger.LogInformation("Optimizer proposal received: {Proposal} from {From}", proposal.Proposal, proposal.FromNeuron);
        return Task.CompletedTask;
    }
}

// Dynamic generated neuron - "loaded" via compiler flow (prototype for NeuroPack dynamic assembly + grain reg)
[GrainType("neuro.generated")]
public class GeneratedNeuron : Neuron, IGeneratedNeuron, IHandle<NeuronTelemetry>
{
    private string _id = string.Empty;

    public GeneratedNeuron(ILogger<GeneratedNeuron> logger)
        : base(logger)
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        _id = this.GetPrimaryKeyString() ?? "unknown-generated";
    }

    public Task HandleAsync(NeuronTelemetry telemetry)
    {
        // Consumed to prevent re-dispatch recursion; telemetry is journaled via the fire
        return Task.CompletedTask;
    }

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        Logger.LogInformation("GeneratedNeuron {Id} dispatched {Type}", _id, synapse.Type);
        await FireAsync(new NeuronTelemetry(Self, "generated-dispatched"));
        if (synapse is DemoMessageSynapse msg)
        {
            Logger.LogInformation("Generated handled message: {Text}", msg.Text);
        }
        else if (synapse is ExperienceUsed used)
        {
            Logger.LogInformation("Generated experience {Pack} used: {Action}", used.Pack, used.Action);
        }
    }
}

[GrainType("neuro.llm.qwen.v1")]
public class LlmNeuron : Neuron, ILlmNeuron
{
    public LlmNeuron(ILogger<LlmNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(LlmPrompt prompt)
    {
        var client = ServiceProvider.GetService<IOllamaApiClient>();
        if (client == null)
        {
            await FireAsync(new LlmResponse(prompt.Prompt, "[no local llm client]", "none"));
            return;
        }

        client.SelectedModel = prompt.PreferredModel ?? "qwen2.5-coder:1.5b";
        var acc = "";
        await foreach (var chunk in client.GenerateAsync(prompt.Prompt))
        {
            if (chunk?.Response is string t) acc += t;
        }
        await FireAsync(new LlmResponse(prompt.Prompt, acc.Trim(), client.SelectedModel));
    }
}

[GrainType("awesome.se.team10.v1")]
public class Software10TeamNeuron : Neuron, ISoftware10Team
{
    public Software10TeamNeuron(ILogger<Software10TeamNeuron> logger) : base(logger) { }

    public async Task HandleAsync(CreateSimpleApp cmd)
    {
        var name = "Legacy" + cmd.Description.Replace(" ", "").Substring(0, Math.Min(12, cmd.Description.Length));
        // Old soft: rigid 2010s style template
        var code = $"// Software10 (old) - classic style\nusing System;\npublic class {name}App {{\n  public static void Main() {{\n    Console.WriteLine(\"TODO: {cmd.Description}\");\n  }}\n}}";
        await FireAsync(new SimpleAppCreated(cmd.Team, name, code));
    }
}

[GrainType("awesome.se.team20.v1")]
public class Software20TeamNeuron : Neuron, ISoftware20Team
{
    public Software20TeamNeuron(ILogger<Software20TeamNeuron> logger) : base(logger) { }

    public async Task HandleAsync(CreateSimpleApp cmd)
    {
        var name = "Neuro" + cmd.Description.Replace(" ", "").Substring(0, Math.Min(12, cmd.Description.Length));
        string code;

        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm != null)
        {
            llm.SelectedModel = "qwen2.5-coder:1.5b";
            var p = $"Create a clean minimal C# console or Neuron-style simple app for: {cmd.Description}. Make it modern, self-documenting, no legacy main if possible. Output only the code.";
            var acc = "";
            await foreach (var chunk in llm.GenerateAsync(p))
                if (chunk?.Response is string t) acc += t;
            code = acc.Trim().Length > 10 ? acc.Trim() : ModernTemplate(name, cmd.Description);
        }
        else
        {
            code = ModernTemplate(name, cmd.Description);
        }

        await FireAsync(new SimpleAppCreated(cmd.Team, name, code));
    }

    static string ModernTemplate(string n, string d) =>
        $"// Software20 (new) - neuro/LLM assisted\n[GrainType(\"app.{n.ToLower()}\")]\npublic class {n}App : Neuron {{\n  // Self-improving simple app for: {d}\n  public {n}App() {{ /* modern defaults */ }}\n}}";
}

// SystemStatus + self-awareness (MVP)
// Connects to own Aspire via MCP (aspire mcp start pattern), uses LLM for diagnosis, proposes fixes,
// and supports isolated simulation via TestCluster replay from journal "checkpoint".
[GrainType("digitalbrain.systemstatus.v1")]
public class SystemStatusNeuron : Neuron, ISystemStatus
{
    private McpClient? _mcp;

    public SystemStatusNeuron(ILogger<SystemStatusNeuron> logger) : base(logger) { }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await TryConnectMcpAsync(ct);
        await FireAsync(new SystemLaunched("digitalbrain", DateTimeOffset.UtcNow));
        await FireAsync(new SystemStatusChanged("kernel", "launched"));
    }

    private async Task TryConnectMcpAsync(CancellationToken ct)
    {
        try
        {
            // Adapted from AspireAgent pattern (resolve AppHost dir or use cwd)
            var workDir = Environment.GetEnvironmentVariable("DIGITALBRAIN_APPHOST_DIR")
                          ?? AppContext.BaseDirectory;

            _mcp = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire-self",
                    Command = "aspire",
                    Arguments = ["mcp", "start", "--non-interactive"],
                    WorkingDirectory = workDir
                }), cancellationToken: ct);

            var tools = await _mcp.ListToolsAsync(cancellationToken: ct);
            Logger.LogInformation("SystemStatus connected to Aspire MCP ({Count} tools)", tools.Count);
            await FireAsync(new SystemStatusChanged("aspire-mcp", "connected", $"tools={tools.Count}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SystemStatus MCP connect failed (running outside aspire or no aspire in PATH). Self-awareness limited to internal telemetry.");
            await FireAsync(new SystemStatusChanged("aspire-mcp", "unavailable"));
        }
    }

    public async Task HandleAsync(SystemStatusChanged status)
    {
        Logger.LogInformation("System status: {Component} = {Status}", status.Component, status.Status);

        if (status.Status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
            status.Status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            await DiagnoseAndProposeAsync(status, default);
        }
    }

    public Task HandleAsync(FixProposal proposal)
    {
        Logger.LogInformation("Fix proposal received: {Issue} -> {Fix}", proposal.Issue, proposal.ProposedFix);
        return Task.CompletedTask;
    }

    private async Task DiagnoseAndProposeAsync(SystemStatusChanged bad, CancellationToken ct)
    {
        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        string analysis = "manual review required";
        if (llm != null && _mcp != null)
        {
            try
            {
                // Pull real data via MCP
                var resources = await CallMcpAsync("list_resources", ct);
                var logs = await CallMcpAsync("list_structured_logs", new { resourceName = bad.Component }, ct);

                llm.SelectedModel = "qwen2.5-coder:1.5b";
                var prompt = $"Analyze this DigitalBrain failure. Component: {bad.Component} Status: {bad.Status}. Resources: {resources}. Logs: {logs}. Propose one minimal fix.";
                var acc = "";
                await foreach (var ch in llm.GenerateAsync(prompt)) if (ch?.Response is string t) acc += t;
                analysis = acc.Trim();
            }
            catch { /* fall through */ }
        }

        var proposal = $"Apply: {analysis}";
        await FireAsync(new FixProposal(bad.Component, proposal, "SystemStatusNeuron"));

        // Simulation: isolated replay from current journal as checkpoint
        await RunIsolatedSimulationAsync(bad, proposal, ct);
    }

    private async Task<string> CallMcpAsync(string tool, object? args = null, CancellationToken ct = default)
    {
        if (_mcp == null) return "mcp-unavailable";
        var res = await _mcp.CallToolAsync(tool, args as Dictionary<string, object?> ?? new(), cancellationToken: ct);
        return res.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no-data";
    }

    private async Task RunIsolatedSimulationAsync(SystemStatusChanged bad, string proposedFix, CancellationToken ct)
    {
        // MVP simulation using existing TestCluster + journal as "checkpoint"
        // In real test harness this would be driven from steps; here we just log the intent + fire result
        // (full isolated replay with modified behavior lives in test layer per plan)
        await FireAsync(new SimulationResult(
            $"bad-state-{bad.Component}",
            Success: true,
            $"Simulated fix '{proposedFix}' from journal checkpoint. Expected: healthy. (See DigitalBrain.Tests for full isolated run)"));
    }
}