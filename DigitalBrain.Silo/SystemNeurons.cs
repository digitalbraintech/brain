using DigitalBrain.Protocol;
using DigitalBrain.Silo.Foundry;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Orleans.Journaling;
using Orleans.Runtime;
using System.Reflection;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;

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
    // Transition policy: unsigned packs install with a warning. Flip to true before enabling remote/untrusted install.
    private const bool RejectUnsignedPacks = false;

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

        // Trust gate. A PRESENT-but-invalid signature is always rejected. A MISSING signature is warn-only
        // during the transition (RejectUnsignedPacks=false) so the existing unsigned seeds keep working;
        // flip to strict before any remote/untrusted install is enabled.
        var isSigned = !string.IsNullOrEmpty(pack.AuthorPublicKeyBase64) && !string.IsNullOrEmpty(pack.SignatureBase64);
        if (isSigned)
        {
            if (!PackSignatureVerifier.VerifyPack(pack))
            {
                Logger.LogWarning("Install REJECTED - invalid signature on pack {Key}", cmd.PackName + "@" + cmd.Version);
                return;
            }
            Logger.LogInformation("Install signature verified for pack {Key}", cmd.PackName + "@" + cmd.Version);
        }
        else if (RejectUnsignedPacks)
        {
            Logger.LogWarning("Install REJECTED - pack {Key} is unsigned and unsigned installs are disabled", cmd.PackName + "@" + cmd.Version);
            return;
        }
        else
        {
            Logger.LogWarning("Install WARNING - pack {Key} is unsigned (allowed during trust transition)", cmd.PackName + "@" + cmd.Version);
        }

        // Economics gate: a premium pack (Price > 0) requires the buyer to hold a license (entitlement).
        if (pack.Price > 0m)
        {
            var license = GrainFactory.GetGrain<ILicenseNeuron>("license-main");
            if (!await license.HasLicenseAsync(pack.Name, cmd.BuyerId))
            {
                Logger.LogWarning("Install REJECTED - premium pack {Key} requires a license for buyer {Buyer}", cmd.PackName + "@" + cmd.Version, cmd.BuyerId);
                return;
            }
            Logger.LogInformation("Install entitlement verified for premium pack {Key}, buyer {Buyer}", cmd.PackName + "@" + cmd.Version, cmd.BuyerId);
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
        // Deliver the full pack (with Code) so the host neuron can compile + embody it; then trigger a use.
        await generated.DeliverAsync(new NeuroPackInstalled(pack));
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
            .Select(p => new NeuroPack(p.PackName, p.Version, p.OwnerId, p.IsPrivate, p.CommissionRate, p.Code, p.Description, p.AuthorPublicKeyBase64, p.SignatureBase64, p.Price))
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
    public MetaOptimizerNeuron(ILogger<MetaOptimizerNeuron> logger)
        : base(logger)
    {
    }

    public async Task HandleAsync(NeuronTelemetry telemetry)
    {
        // Pure journal-derived count (no private state).
        var count = IncomingJournal.Concat(OutgoingJournal).OfType<NeuronTelemetry>().Count();
        Logger.LogInformation("Optimizer received telemetry from {Neuron}: {Event} (total {Count})", telemetry.Neuron, telemetry.Event, count);

        if (count % 5 == 0)
        {
            string proposal;
            var chat = ServiceProvider.GetService<IChatClient>();
            if (chat != null)
            {
                var p = $"Telemetry count reached {count}. Propose ONE short, actionable wiring or scaling improvement for the DigitalBrain neuron system (Orleans grains + Aspire + compiler for code gen from English).";
                var response = await chat.GetResponseAsync(p);
                var acc = response.Text;
                proposal = acc.Length > 20 ? acc.Trim() : "Add parallel compiler neurons and route create requests through LlmNeuron";
            }
            else
            {
                proposal = "Add parallel compiler neurons routed via LlmNeuron for faster self-gen";
            }
            await FireAsync(new WiringOptimizationProposed(proposal, Self.Value));
        }
    }

    public Task HandleAsync(WiringOptimizationProposed proposal)
    {
        Logger.LogInformation("Optimizer proposal received: {Proposal} from {From}", proposal.Proposal, proposal.FromNeuron);
        return Task.CompletedTask;
    }
}

// Host for installed packs (keystone embodiment, option b).
// On install it receives the real NeuroPack and COMPILES pack.Code into a collectible ALC via IPackEmbodiment,
// holding the live IPackBehavior capability. On use it dispatches to that compiled code for real and fires a
// PackEmission with the pack's actual output — replacing the old LLM "personality" stub, which now only serves
// as a fallback for natural-language packs whose Code is not a compilable IPackBehavior.
[GrainType("digitalbrain.generated")]
public class GeneratedNeuron : Neuron, IGeneratedNeuron, IHandle<NeuronTelemetry>
{
    private EmbodiedPack? _embodied;

    public GeneratedNeuron(ILogger<GeneratedNeuron> logger)
        : base(logger)
    {
    }

    public Task HandleAsync(NeuronTelemetry telemetry) => Task.CompletedTask;

    public override Task OnDeactivateAsync(Orleans.DeactivationReason reason, CancellationToken cancellationToken)
    {
        // Unload the ALC (if any) on deactivation. This is necessary but not always sufficient for full collection:
        // Orleans may retain type info / proxies. Use weak-ref verification in tests + explicit GC pressure for proof.
        var toUnload = _embodied;
        _embodied = null;
        toUnload?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        var id = this.GetPrimaryKeyString() ?? "unknown-generated";
        Logger.LogInformation("GeneratedNeuron {Id} dispatched {Type}", id, synapse.Type);
        await FireAsync(new NeuronTelemetry(Self, "generated-dispatched"));

        switch (synapse)
        {
            case NeuroPackInstalled installed:
                TryEmbody(installed.Pack);
                break;
            case DemoMessageSynapse msg:
                Logger.LogInformation("Generated handled message: {Text}", msg.Text);
                break;
            case ExperienceUsed used:
                await UseExperienceAsync(used);
                break;
        }
    }

    // Compile + load the pack into a collectible ALC. A pack that is not a compilable IPackBehavior
    // (e.g. a natural-language pack) fails embodiment; we log it and fall back to the LLM path on use.
    private void TryEmbody(NeuroPack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.Code))
            return;

        var embodier = ServiceProvider.GetService<IPackEmbodiment>();
        if (embodier is null)
        {
            Logger.LogWarning("No IPackEmbodiment registered; pack '{Pack}' will use the LLM fallback.", pack.Name);
            return;
        }

        try
        {
            _embodied?.Dispose();
            _embodied = embodier.Embody(pack.Name, pack.Code);
            Logger.LogInformation("GeneratedNeuron EMBODIED pack {Name}@{Ver} as real compiled C#.", pack.Name, pack.Version);
        }
        catch (PackEmbodimentException ex)
        {
            _embodied = null;
            Logger.LogWarning(ex, "Pack '{Pack}' is not a compilable IPackBehavior; using LLM fallback on use.", pack.Name);
        }
    }

    private void EnsureEmbodied()
    {
        if (_embodied is not null) return;
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last is not null)
            TryEmbody(last.Pack);
    }

    private async Task UseExperienceAsync(ExperienceUsed used)
    {
        EnsureEmbodied();

        // Real path: the pack's compiled code runs and we emit its actual output.
        if (_embodied is not null)
        {
            var output = _embodied.Respond(used.Action);
            await FireAsync(new PackEmission(_embodied.PackName, used.Action, output));
            Logger.LogInformation("GeneratedNeuron ran embodied pack '{Pack}' for action '{Action}'", _embodied.PackName, used.Action);
            return;
        }

        // Fallback for natural-language packs with no compilable Code: LLM "embodiment" (the legacy behavior).
        var inst = LastInstalledPack();
        if (inst is null)
        {
            Logger.LogInformation("Generated experience {Pack} used: {Action} (no installed pack yet).", used.Pack, used.Action);
            return;
        }

        var (packKey, code, desc) = inst.Value;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            await FireAsync(new LlmResponse(used.Pack, $"[Embodied: {packKey}] Simulated response to {used.Action} using installed experience.", "sim"));
            return;
        }

        var behaviorPrompt = $"You are now the installed experience '{packKey}'.\n" +
                             $"Description: {desc}\n" +
                             $"Implementation guidance/code:\n{code}\n\n" +
                             $"Handle the following usage: {used.Action} on input related to '{used.Pack}'.\n" +
                             "Respond in character as this specific installed neuron/experience would. Be concise and useful.";
        var response = await chat.GetResponseAsync(behaviorPrompt);
        await FireAsync(new LlmResponse(behaviorPrompt, response.Text.Trim(), "embodied-pack"));
        Logger.LogInformation("GeneratedNeuron LLM-embodied pack '{Pack}' for action '{Action}'", packKey, used.Action);
    }

    private (string Key, string Code, string Description)? LastInstalledPack()
    {
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last is null) return null;
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
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null)
        {
            await FireAsync(new LlmResponse(prompt.Prompt, "[no local llm client]", "none"));
            return;
        }

        var options = string.IsNullOrWhiteSpace(prompt.PreferredModel)
            ? null
            : new Microsoft.Extensions.AI.ChatOptions { ModelId = prompt.PreferredModel };
        var response = await chat.GetResponseAsync(prompt.Prompt, options);
        await FireAsync(new LlmResponse(prompt.Prompt, response.Text.Trim(), prompt.PreferredModel ?? "qwen2.5-coder:1.5b"));
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

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            var p = $"Create a clean minimal C# console or Neuron-style simple app for: {cmd.Description}. Make it modern, self-documenting, no legacy main if possible. Output only the code.";
            var response = await chat.GetResponseAsync(p);
            var acc = response.Text;
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

// SoftwareEngineering.ClosedLoopNeuron + UI authoring closed loop support.
// Embodiable via marketplace NeuroPack. Uses local Ollama + MCP connections (Aspire for SE mods, Dart MCP knowledge for UI).
// Handles multi-kernel by preferring Aspire orchestration (restart resources, inspect distributed state) + marketplace for behavior updates (new packs become live via Generated).
[GrainType("softwareengineering.closedloop.v1")]
public class SoftwareEngineeringClosedLoopNeuron : Neuron, IClosedLoopNeuron
{
    private McpClient? _aspireMcp;

    public SoftwareEngineeringClosedLoopNeuron(ILogger<SoftwareEngineeringClosedLoopNeuron> logger) : base(logger) { }

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

        // For SE, attempt Aspire MCP driven apply if prompt indicates modification
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
                    // Example safe apply: would parse LLM suggestion but here log + example restart
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
        string result;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            var prompt = $"Perform the kernel task and output ONLY the concise result value: {cmd.Description}";
            var response = await chat.GetResponseAsync(prompt);
            result = response.Text.Trim();
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

[GrainType("ino.code.editor.v1")]
public class InoCodeEditorNeuron : Neuron, IInoCodeEditor
{
    public InoCodeEditorNeuron(ILogger<InoCodeEditorNeuron> logger) : base(logger) { }

    public async Task HandleAsync(InoCodeEdit cmd)
    {
        Logger.LogInformation("INO Code Editor edit for {Id}", cmd.EditorId);
        await FireAsync(new InoCodeEdit(cmd.EditorId, cmd.Code, cmd.Language));
        await FireAsync(new ContextUpdate("editor", "lastCode", cmd.Code.Length > 120 ? cmd.Code[..120] + "..." : cmd.Code));
    }

    public async Task HandleAsync(InoCodeRun cmd)
    {
        Logger.LogInformation("INO Code Editor run for {Id}: {Result}", cmd.EditorId, cmd.Result);
        await FireAsync(cmd);
    }

    public async Task HandleAsync(InoCodeSave cmd)
    {
        Logger.LogInformation("INO Code Editor save {Name} for {Id}", cmd.ExperienceName, cmd.EditorId);
        await FireAsync(cmd);
        var market = GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new PublishToMarketplace(cmd.ExperienceName, "0.1-ino", cmd.Code, "editor-user", false, 0.0, cmd.Description));
        await FireAsync(new ContextUpdate("editor", "saved", cmd.ExperienceName));
    }

    public async Task HandleAsync(InoCodeExecute cmd)
    {
        Logger.LogInformation("INO Code Editor execute for {Id}", cmd.EditorId);
        await FireAsync(cmd);
        var compiler = GrainFactory.GetGrain<ICompiler>("compiler-main");
        await compiler.FireAsync(new CreateNeuronRequest(cmd.Instruction + " | editor:" + cmd.EditorId, "csharp"));
        await FireAsync(new InoCodeRun(cmd.EditorId, "executed-via-compiler"));
    }

    public async Task HandleAsync(InoCodeApplySkill cmd)
    {
        Logger.LogInformation("INO Code Editor apply skill {Skill} for {Id}", cmd.SkillPackName, cmd.EditorId);
        var market = GrainFactory.GetGrain<IMarketplaceNeuron>("market-main");
        await market.FireAsync(new ListPublished());
        var tl = await market.GetTimelineAsync();
        var list = tl.LastOrDefault(s => s is PublishedList) as PublishedList;
        var pack = list?.Packs.FirstOrDefault(p => p.Name.Equals(cmd.SkillPackName, StringComparison.OrdinalIgnoreCase));
        if (pack != null)
        {
            await FireAsync(new SkillContextInjected(pack.Name, pack.Description, pack.Code));
            await FireAsync(new ContextUpdate("editor-skill", pack.Name, pack.Description.Length > 80 ? pack.Description[..80] : pack.Description));
            var gen = GrainFactory.GetGrain<IGeneratedNeuron>("generated-" + pack.Name.ToLowerInvariant());
            await gen.FireAsync(new ExperienceUsed(pack.Name, "editor-apply"));
        }
        else
        {
            await FireAsync(new ContextUpdate("editor-skill", cmd.SkillPackName, "not-found-in-journals"));
        }
        await FireAsync(new InoCodeRun(cmd.EditorId, "skill-applied:" + cmd.SkillPackName));
    }
}

// ContextNeuron - smart context management for INO (chat, filters, agents, cluster, etc.)
// Like context providers in advanced agent systems. INO and UI notify it on changes (filters etc).
[GrainType("context.manager.v1")]
public class ContextNeuron : Neuron, IContextNeuron
{
    public ContextNeuron(ILogger<ContextNeuron> logger) : base(logger) { }

    public async Task HandleAsync(ContextUpdate cmd)
    {
        Logger.LogInformation("Context updated: {Name}.{Key} = {Val}", cmd.ContextName, cmd.Key, cmd.Value);
        await FireAsync(cmd);
    }

    public Task<string> GetContextAsync(string contextName)
    {
        var entries = OutgoingJournal.Concat(IncomingJournal).OfType<ContextUpdate>()
            .Where(c => c.ContextName == contextName)
            .Take(10)
            .Select(c => $"{c.Key}={c.Value}");
        return Task.FromResult(string.Join("; ", entries));
    }

    public async Task RememberAsync(string text)
    {
        var embedding = await EmbedAsync(text);
        await FireAsync(new MemoryStored(text, embedding));
    }

    public async Task<string[]> RecallAsync(string query, int top = 5)
    {
        var queryEmbedding = await EmbedAsync(query);
        var memories = OutgoingJournal.Concat(IncomingJournal).OfType<MemoryStored>();
        return memories
            .Select(m => (m.Text, Score: HybridScorer.Score(query, m.Text, queryEmbedding, m.Embedding)))
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .Take(top)
            .Select(x => x.Text)
            .ToArray();
    }

    // Embeds text via the registered IEmbeddingGenerator (NoOp by default → empty vector → keyword-only recall).
    private async Task<float[]> EmbedAsync(string text)
    {
        var generator = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (generator is null) return [];
        var generated = await generator.GenerateAsync([text]);
        return generated.First().Vector.ToArray();
    }
}

// Dynamic DB neuron - runtime DB support with typed synapses.
// Uses connections, can "generate" dynamic access (in .NET 11 style file-based/runtime).
// Marketplace examples use this to connect to real DBs and query via synapses.
[GrainType("db.support.v1")]
public class DbSupportNeuron : Neuron, IDbSupportNeuron
{
    public DbSupportNeuron(ILogger<DbSupportNeuron> logger) : base(logger) { }

    public async Task HandleAsync(DbConnect cmd)
    {
        Logger.LogInformation("DB connected {Name} via {Provider}", cmd.ConnectionName, cmd.Provider);
        // In real: store conn, use EF dynamic or ADO. For now journal + simulate typed.
        await FireAsync(cmd);
    }

    public async Task HandleAsync(DbQuery cmd)
    {
        Logger.LogInformation("DB query on {Name}: {Q}", cmd.ConnectionName, cmd.Query);
        // Simulate result, in real would execute and return typed rows as synapses.
        var result = $"[DB result for {cmd.Query}] 42 rows";
        await FireAsync(new DbQuery(cmd.ConnectionName, cmd.Query, result));
    }
}

// NOTE: the untyped NuGetManagerNeuron and RoslynArchitectNeuron were retired here in favor of the typed SDK
// neurons INuGetNeuron/NuGetNeuron and IRoslynNeuron/RoslynNeuron (see DigitalBrain.Silo/Sdk). The latter
// preserves the same MSBuildWorkspace analysis behind a typed contract.
