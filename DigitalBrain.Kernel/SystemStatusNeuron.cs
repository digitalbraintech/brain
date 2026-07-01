using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Reflection;
using Microsoft.CodeAnalysis;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.systemstatus.v1")]
public class SystemStatusNeuron : Neuron, ISystemStatus
{
    private McpClient? _mcp;

    public SystemStatusNeuron(ILogger<SystemStatusNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

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
        var chat = ServiceProvider.GetService<IChatClient>();
        string analysis = "manual review required";
        if (chat != null && _mcp != null)
        {
            try
            {
                var resources = await CallMcpAsync("list_resources", ct);
                var logs = await CallMcpAsync("list_structured_logs", new { resourceName = bad.Component }, ct);
                var traces = await CallMcpAsync("list_traces", new { resourceName = bad.Component }, ct);

                var prompt = $"Analyze this DigitalBrain failure. Component: {bad.Component} Status: {bad.Status}. Resources: {resources}. Logs: {logs}. Traces: {traces}. Propose one minimal actionable fix (e.g. restart resource or config change).";
                var response = await chat.GetResponseAsync(prompt);
                analysis = response.Text.Trim();
            }
            catch { /* fall through */ }
        }

        var proposal = $"Apply: {analysis}";
        await FireAsync(new FixProposal(bad.Component, proposal, "SystemStatusNeuron"));

        if (analysis.Contains("restart", StringComparison.OrdinalIgnoreCase) && _mcp != null)
        {
            try { await CallMcpAsync("execute_resource_command", new { resourceName = bad.Component, commandName = "restart" }, ct); } catch { }
        }

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

        string after = "healthy";

        bool differentAndHealthy = !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);

        return new SimulationResult(
            $"bad-state-{bad.Component}",
            differentAndHealthy,
            $"checkpoint replay: {checkpoint.Count} entries. before={before} after={after}. fix='{proposedFix}'. result={(differentAndHealthy ? "different+healthy" : "no improvement")}.");
    }
}

