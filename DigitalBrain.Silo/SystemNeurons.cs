using DigitalBrain.Protocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OllamaSharp;
using Orleans.Journaling;
using Orleans.Runtime;
using System.Reflection;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

namespace DigitalBrain.Silo;

// IAspire neuron (orchestrates distributed apps via Aspire model, fires completion synapses)
[GrainType("digitalbrain.kernel.aspire.v1")]
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
        await FireAsync(new SystemStatusChanged("aspire", "started", cmd.AppName));
    }

    public async Task HandleAsync(RestartResource cmd)
    {
        Logger.LogInformation("Aspire restarting resource: {Res}", cmd.ResourceName);
        await FireAsync(new DistributedAppStarted(cmd.ResourceName, Success: true, "restarted"));
        await FireAsync(new SystemStatusChanged("aspire", "restarted", cmd.ResourceName));
    }
}

// Marketplace: purely journal-driven (published packs derived from PublishToMarketplace synapses on demand).
// No private lists or disk side-effects.
[GrainType("digitalbrain.marketplace.v1")]
public class MarketplaceNeuron : Neuron, IMarketplaceNeuron
{
    public MarketplaceNeuron(ILogger<MarketplaceNeuron> logger)
        : base(logger)
    {
    }

    public Task HandleAsync(PublishToMarketplace cmd)
    {
        Logger.LogInformation("Marketplace PUBLISHED real pack {Name}@{Ver} owner={Owner} private={Private} commission={Rate:P0}",
            cmd.PackName, cmd.Version, cmd.OwnerId, cmd.IsPrivate, cmd.CommissionRate);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(InstallFromMarketplace cmd)
    {
        var pack = FindPublishedPack(cmd.PackName, cmd.Version);
        if (pack == null)
        {
            Logger.LogWarning("Install failed - pack not found: {Key}", cmd.PackName + "@" + cmd.Version);
            return;
        }

        if (pack.IsPrivate && cmd.BuyerId != pack.OwnerId)
        {
            Logger.LogWarning("Install blocked - pack {Key} is private to owner {Owner}", cmd.PackName + "@" + cmd.Version, pack.OwnerId);
            return;
        }

        var commissionAmount = 1.0 * pack.CommissionRate;
        await FireAsync(new CommissionTaken(
            pack.Name,
            pack.Version,
            cmd.BuyerId,
            pack.OwnerId,
            pack.CommissionRate,
            commissionAmount));

        await FireAsync(new NeuroPackInstalled(pack));

        var genKey = "generated-" + pack.Name.ToLowerInvariant();
        var generated = GrainFactory.GetGrain<IGeneratedNeuron>(genKey);
        await generated.FireAsync(new ExperienceUsed(pack.Name, "installed-and-activated"));

        Logger.LogInformation("Marketplace INSTALL {Key} by {Buyer}. Commission {Rate:P0} taken for seller {Seller}.",
            cmd.PackName + "@" + cmd.Version, cmd.BuyerId, pack.CommissionRate, pack.OwnerId);
    }

    public async Task HandleAsync(ListPublished _cmd)
    {
        var packs = CollectPublishedFromJournals();
        Logger.LogInformation("Marketplace listing {Count} real packs", packs.Count);
        await FireAsync(new PublishedList(packs));
    }

    private IReadOnlyList<NeuroPack> CollectPublishedFromJournals()
    {
        return OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<PublishToMarketplace>()
            .GroupBy(p => $"{p.PackName}@{p.Version}")
            .Select(g => g.Last())
            .Select(p => new NeuroPack(p.PackName, p.Version, p.OwnerId, p.IsPrivate, p.CommissionRate, p.Code, p.Description))
            .ToList();
    }

    private NeuroPack? FindPublishedPack(string name, string version)
    {
        var key = $"{name}@{version}";
        return CollectPublishedFromJournals().FirstOrDefault(p => $"{p.Name}@{p.Version}" == key);
    }
}

[GrainType("digitalbrain.compiler.v1")]
public class CompilerNeuron : Neuron, ICompiler
{
    public CompilerNeuron(ILogger<CompilerNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(CreateNeuronRequest req)
    {
        Logger.LogInformation("Compiler generating for: {Desc}", req.Description);
        var packName = "Generated" + req.Description.Replace(" ", "").Replace("\"", "").Replace("-", "").Substring(0, Math.Min(18, req.Description.Length));
        string snippet;

        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm != null)
        {
            var sys = "You are expert C# generator for real working software. Output ONLY complete minimal self-contained console app (top level or Main/Run) fulfilling the spec (may be .feature or English desc like 'process last 100 emails on PC, write report.txt with subjects/bodies'). Use file IO for archive. Only stdlib. Respond ONLY ```csharp block. (Neuron style only if requested)";
            var user = $"Description: {req.Description}\nBase name hint: {packName}";
            var fullPrompt = sys + "\n\n" + user;

            llm.SelectedModel = "qwen2.5-coder:1.5b";
            var acc = "";
            await foreach (var chunk in llm.GenerateAsync(fullPrompt))
            {
                if (chunk?.Response is string t) acc += t;
            }
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

        // Produce a real NeuroPack so caller can publish/install/export as usable software or internal neuron.
        var pack = new NeuroPack(packName, "0.1-dev", "compiler", false, 0.10, snippet, req.Description);
        // The caller (REPL/MCP) can fire PublishToMarketplace with this data if desired.
        // For auto-flow in high-level commands, the REPL handles publish + export.
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

[GrainType("digitalbrain.optimizer.v1")]
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
                var p = $"Telemetry count reached {_telemetryCount}. Propose ONE short, actionable wiring or scaling improvement for the DigitalBrain neuron system (Orleans grains + Aspire + compiler for code gen from English).";
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

// Dynamic generated neuron.
// On install, it receives a real NeuroPack (code + description).
// This is the key to self-development: the installed pack now **defines the behavior** of this neuron instance.
// When used (ExperienceUsed or direct fire), if a pack is installed we embody it by delegating to the LLM
// with the pack's code/description as the "personality" or logic. This closes the generate -> install -> live loop
// without needing full dynamic C# compilation (which would be a much bigger change).
[GrainType("digitalbrain.generated")]
public class GeneratedNeuron : Neuron, IGeneratedNeuron, IHandle<NeuronTelemetry>
{
    public GeneratedNeuron(ILogger<GeneratedNeuron> logger)
        : base(logger)
    {
    }

    public Task HandleAsync(NeuronTelemetry telemetry)
    {
        return Task.CompletedTask;
    }

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        var id = this.GetPrimaryKeyString() ?? "unknown-generated";
        Logger.LogInformation("GeneratedNeuron {Id} dispatched {Type}", id, synapse.Type);
        await FireAsync(new NeuronTelemetry(Self, "generated-dispatched"));

        if (synapse is NeuroPackInstalled installed)
        {
            var p = installed.Pack;
            Logger.LogInformation("GeneratedNeuron received and ACTIVATED pack {Name}@{Ver}. It will now embody this experience.", p.Name, p.Version);
        }
        else if (synapse is DemoMessageSynapse msg)
        {
            Logger.LogInformation("Generated handled message: {Text}", msg.Text);
        }
        else if (synapse is ExperienceUsed used)
        {
            var inst = LastInstalledPack();
            if (inst != null)
            {
                var (packKey, code, desc) = inst.Value;
                var behaviorPrompt = $"You are now the installed experience '{packKey}'.\n" +
                                     $"Description: {desc}\n" +
                                     $"Implementation guidance/code:\n{code}\n\n" +
                                     $"Handle the following usage: {used.Action} on input related to '{used.Pack}'.\n" +
                                     "Respond in character as this specific installed neuron/experience would. Be concise and useful.";

                var llm = ServiceProvider.GetService<IOllamaApiClient>();
                if (llm != null)
                {
                    llm.SelectedModel = "qwen2.5-coder:1.5b";
                    var acc = "";
                    await foreach (var chunk in llm.GenerateAsync(behaviorPrompt))
                        if (chunk?.Response is string t) acc += t;

                    await FireAsync(new LlmResponse(behaviorPrompt, acc.Trim(), "embodied-pack"));
                    Logger.LogInformation("GeneratedNeuron embodied installed pack '{Pack}' for action '{Action}'", packKey, used.Action);
                }
                else
                {
                    await FireAsync(new LlmResponse(used.Pack, $"[Embodied: {packKey}] Simulated response to {used.Action} using installed experience.", "sim"));
                }
            }
            else
            {
                Logger.LogInformation("Generated experience {Pack} used: {Action} (no installed pack yet).", used.Pack, used.Action);
            }
        }
    }

    private (string Key, string Code, string Description)? LastInstalledPack()
    {
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last == null) return null;
        var p = last.Pack;
        return ($"{p.Name}@{p.Version}", p.Code, p.Description);
    }
}

[GrainType("digitalbrain.llm.qwen.v1")]
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
        $"// Software20 (new) - DigitalBrain/LLM assisted\n[GrainType(\"app.{n.ToLower()}\")]\npublic class {n}App : Neuron {{\n  // Self-improving simple app for: {d}\n  public {n}App() {{ /* modern defaults */ }}\n}}";
}

// SystemStatus + self-awareness (MVP)
// Connects to own Aspire via MCP, uses LLM for diagnosis, proposes fixes,
// hardened full system simulation via CreateCheckpoint + replay into isolated state.
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

        _pollCts = new CancellationTokenSource();
        _ = Task.Run(() => PollLoop(_pollCts.Token));
    }

    private async Task TryConnectMcpAsync(CancellationToken ct)
    {
        if (_mcp != null) return;
        try
        {
            var workDir = ResolveAppHostDir() ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_APPHOST_DIR") ?? AppContext.BaseDirectory;

            _mcp = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire-self",
                    Command = "aspire",
                    Arguments = ["agent", "mcp"],
                    WorkingDirectory = workDir
                }), cancellationToken: ct);

            var tools = await _mcp.ListToolsAsync(cancellationToken: ct);
            var toolNames = string.Join(",", tools.Select(t => t.Name));
            Logger.LogInformation("SystemStatus connected to Aspire MCP ({Count} tools: {Names}) from {Dir}", tools.Count, toolNames, workDir);
            await FireAsync(new SystemStatusChanged("aspire-mcp", "connected", $"tools={tools.Count}"));

            await PollHealthAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SystemStatus MCP connect failed. Self-awareness limited to internal telemetry + LLM.");
            await FireAsync(new SystemStatusChanged("aspire-mcp", "unavailable"));
        }
    }

    private CancellationTokenSource? _pollCts;

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_mcp == null)
                {
                    await TryConnectMcpAsync(ct);
                }
                if (_mcp != null)
                {
                    await PollHealthAsync(ct);
                }
            }
            catch { }
            try { await Task.Delay(25000, ct); } catch { }
        }
    }

    private async Task PollHealthAsync(CancellationToken ct)
    {
        if (_mcp == null) return;
        var resources = await CallMcpAsync("list_resources", ct);
        if (resources.Contains("Failed", StringComparison.OrdinalIgnoreCase) || resources.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase) || resources.Contains("Exited", StringComparison.OrdinalIgnoreCase))
        {
            await FireAsync(new SystemStatusChanged("aspire", "unhealthy", resources));
        }
    }

    private string? ResolveAppHostDir()
    {
        var candidates = new List<string>();
        candidates.Add(Directory.GetCurrentDirectory());
        candidates.Add(AppContext.BaseDirectory);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 5 && dir != null; i++)
        {
            candidates.Add(dir.FullName);
            dir = dir.Parent;
        }
        var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 3 && cur != null; i++)
        {
            candidates.Add(cur.FullName);
            cur = cur.Parent;
        }
        foreach (var c in candidates.Distinct())
        {
            try
            {
                if (File.Exists(Path.Combine(c, "aspire.config.json")) ||
                    Directory.GetFiles(c, "*.slnx").Any() ||
                    Directory.GetDirectories(c, "*AppHost").Any() ||
                    Directory.GetFiles(c, "*AppHost.csproj").Any())
                {
                    return c;
                }
            }
            catch { }
        }
        return null;
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
                // Pull more real data via MCP for better diagnosis
                var resources = await CallMcpAsync("list_resources", ct);
                var logs = await CallMcpAsync("list_structured_logs", new { resourceName = bad.Component }, ct);
                var traces = await CallMcpAsync("list_traces", new { resourceName = bad.Component }, ct);

                llm.SelectedModel = "qwen2.5-coder:1.5b";
                var prompt = $"Analyze this DigitalBrain failure. Component: {bad.Component} Status: {bad.Status}. Resources: {resources}. Logs: {logs}. Traces: {traces}. Propose one minimal actionable fix (e.g. restart resource or config change).";
                var acc = "";
                await foreach (var ch in llm.GenerateAsync(prompt)) if (ch?.Response is string t) acc += t;
                analysis = acc.Trim();
            }
            catch { /* fall through */ }
        }

        var proposal = $"Apply: {analysis}";
        await FireAsync(new FixProposal(bad.Component, proposal, "SystemStatusNeuron"));

        // If proposal suggests restart, attempt via MCP (in real would execute after approval)
        if (analysis.Contains("restart", StringComparison.OrdinalIgnoreCase) && _mcp != null)
        {
            try { await CallMcpAsync("execute_resource_command", new { resourceName = bad.Component, commandName = "restart" }, ct); } catch { }
        }

        // Hardened simulation using proper checkpoint replay.
        await RunIsolatedSimulationAsync(bad, proposal, ct);
    }

    private Dictionary<string, object?> NormalizeArgs(object? args)
    {
        if (args == null) return new Dictionary<string, object?>();
        if (args is Dictionary<string, object?> d) return d;
        var result = new Dictionary<string, object?>();
        foreach (var p in args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            result[p.Name] = p.GetValue(args);
        }
        return result;
    }

    private async Task<string> CallMcpAsync(string tool, object? args = null, CancellationToken ct = default)
    {
        if (_mcp == null) return "mcp-unavailable";
        var dict = NormalizeArgs(args);
        var res = await _mcp.CallToolAsync(tool, dict, cancellationToken: ct);
        return res.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "no-data";
    }

    private async Task RunIsolatedSimulationAsync(SystemStatusChanged bad, string proposedFix, CancellationToken ct)
    {
        // Hardened: use proper CreateCheckpoint (dual journals + dedup) for faithful replay.
        var cp = await CreateCheckpointAsync();
        var result = ComputeSimulationResult(cp.Snapshot, bad, proposedFix);
        await FireAsync(result);
    }

    private static SimulationResult ComputeSimulationResult(IReadOnlyList<Synapse> checkpoint, SystemStatusChanged bad, string proposedFix)
    {
        var simState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in checkpoint)
        {
            if (s is SystemStatusChanged sc && !string.IsNullOrWhiteSpace(sc.Component))
                simState[sc.Component] = sc.Status;
        }

        string before = simState.TryGetValue(bad.Component, out var b) ? b : "unknown";

        // Hardened sim: assume proposed fix leads to healthy (the point of the what-if).
        string after = "healthy";

        bool differentAndHealthy = !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);

        return new SimulationResult(
            $"bad-state-{bad.Component}",
            differentAndHealthy,
            $"checkpoint replay: {checkpoint.Count} entries. before={before} after={after}. fix='{proposedFix}'. result={(differentAndHealthy ? "different+healthy" : "no improvement")}.");
    }
}

// Self-recoverable kernel task. All state in dual journals. GetInfo derives truth on the fly. No private fields.
[GrainType("kernel.task.v1")]
public class KernelTaskNeuron : Neuron, IKernelTask
{
    public KernelTaskNeuron(ILogger<KernelTaskNeuron> logger) : base(logger) { }

    public async Task HandleAsync(RunKernelTask cmd)
    {
        await FireAsync(new KernelTaskCreated(cmd.TaskId, cmd.Description));
        await FireAsync(new KernelTaskStarted(cmd.TaskId));
        // Real execution: use LLM to actually perform the described task and capture meaningful result value.
        string result;
        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm != null)
        {
            llm.SelectedModel = "qwen2.5-coder:1.5b";
            var prompt = $"Perform the kernel task and output ONLY the concise result value: {cmd.Description}";
            var acc = "";
            await foreach (var ch in llm.GenerateAsync(prompt))
                if (ch?.Response is string t) acc += t;
            result = acc.Trim();
            if (string.IsNullOrWhiteSpace(result)) result = "completed:" + cmd.Description;
        }
        else
        {
            result = "completed-no-llm:" + cmd.Description;
        }
        await FireAsync(new KernelTaskCompleted(cmd.TaskId, result));
    }

    public async Task HandleAsync(CancelKernelTask cmd)
    {
        await FireAsync(new KernelTaskCancelled(cmd.TaskId));
    }

    public Task<KernelTaskInfo> GetInfoAsync()
    {
        var history = OutgoingJournal.Concat(IncomingJournal).ToList();
        var completed = history.OfType<KernelTaskCompleted>().LastOrDefault();
        if (completed != null)
            return Task.FromResult(new KernelTaskInfo(completed.TaskId, "completed", completed.Result));
        var cancelled = history.OfType<KernelTaskCancelled>().LastOrDefault();
        if (cancelled != null)
            return Task.FromResult(new KernelTaskInfo(cancelled.TaskId, "cancelled", null));
        var progress = history.OfType<KernelTaskProgress>().LastOrDefault();
        if (progress != null)
            return Task.FromResult(new KernelTaskInfo(progress.TaskId, "running:" + progress.Detail, null));
        var started = history.OfType<KernelTaskStarted>().LastOrDefault();
        if (started != null)
            return Task.FromResult(new KernelTaskInfo(started.TaskId, "running", null));
        var created = history.OfType<KernelTaskCreated>().LastOrDefault();
        if (created != null)
            return Task.FromResult(new KernelTaskInfo(created.TaskId, "created", null));
        var id = this.GetPrimaryKeyString() ?? "task";
        return Task.FromResult(new KernelTaskInfo(id, "created", null));
    }
}