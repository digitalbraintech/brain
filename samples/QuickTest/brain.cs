// dotnet run --project samples/QuickTest
// Fast entry for practical software creation: create-software -> run/export -> self-improve loop.
// --mcp for agents. Core via grains + Roslyn execution.

using DigitalBrain.Protocol;
using DigitalBrain.Silo;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;

var isMcpMode = args.Contains("--mcp");

var builder = Host.CreateApplicationBuilder(args);

// === ENCAPSULATED + FAST BOOT ===
// Use kernel (silo). For ultra-fast single-process "dotnet run", we use grains directly (no separate client needed).
// This deletes friction: no Redis required for the basic self-dev experience.
builder.UseDigitalBrainKernel();

if (isMcpMode)
{
    builder.Logging.AddConsole(c => c.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<BrainMcpTools>();
}

var app = builder.Build();
await app.StartAsync();

var grains = app.Services.GetRequiredService<IGrainFactory>();

// Bootstrap key neurons
_ = grains.GetGrain<ISystemStatus>("status-main").GetTimelineAsync();
_ = grains.GetGrain<IInoNeuron>("ino-main").GetTimelineAsync();

if (isMcpMode)
{
    Console.Error.WriteLine("DigitalBrain MCP server ready. Cluster (kernel) is live.");
    await app.WaitForShutdownAsync();
}
else
{
    Console.WriteLine("=== DIGITALBRAIN BOOTED ===");
    Console.WriteLine("create-software 'Feature: EmailArchiver on PC - process/write last 100 emails to report.txt' | run | export | self-improve | list | help");

    string? lastGeneratedCode = null;
    string? lastGeneratedDesc = null;

    while (true)
    {
        Console.Write("brain> ");
        var line = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(line) || line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

        try
        {
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0].ToLower())
            {
                case "create-software":
                case "make-automation":
                    if (parts.Length > 1)
                    {
                        var desc = string.Join(' ', parts[1..]);
                        if (lastGeneratedCode != null)
                            desc += "\nRefine previous code:\n" + lastGeneratedCode;
                        var compiler = grains.GetGrain<ICompiler>("compiler-main");
                        await compiler.FireAsync(new CreateNeuronRequest("Create a complete runnable C# console app fulfilling spec (may be feature text for email processing on PC): " + desc));
                        var tl = await compiler.GetTimelineAsync();
                        var genEvt = tl.LastOrDefault(s => s is NeuronCodeGenerated) as NeuronCodeGenerated;
                        if (genEvt != null)
                        {
                            lastGeneratedCode = genEvt.GeneratedCodeSnippet;
                            lastGeneratedDesc = parts[1];
                            Console.WriteLine("Generated:\n" + lastGeneratedCode);
                            CodeRunner.MaterializeAsProject(parts[1], lastGeneratedCode);
                        }
                    }
                    break;

                case "export":
                    {
                        string? code = lastGeneratedCode;
                        string nameForDir = lastGeneratedDesc ?? "Generated";
                        if (parts.Length > 1)
                        {
                            var n = parts[1];
                            var mkt = grains.GetGrain<IMarketplaceNeuron>("market-main");
                            await mkt.FireAsync(new ListPublished());
                            var tl = await mkt.GetTimelineAsync();
                            var pl = tl.LastOrDefault(s => s is PublishedList) as PublishedList;
                            var p = pl?.Packs.FirstOrDefault(x => x.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
                            if (p != null)
                            {
                                code = p.Code;
                                nameForDir = p.Name;
                            }
                        }
                        if (code != null)
                            CodeRunner.MaterializeAsProject(nameForDir, code);
                        else
                            Console.WriteLine("Nothing to export. Use create-software first or export <name>");
                    }
                    break;

                case "run":
                case "execute":
                    {
                        string? code = lastGeneratedCode;
                        string inp = "";
                        if (parts.Length > 1)
                        {
                            var n = parts[1];
                            inp = parts.Length > 2 ? string.Join(' ', parts[2..]) : "";
                            var mkt = grains.GetGrain<IMarketplaceNeuron>("market-main");
                            await mkt.FireAsync(new ListPublished());
                            var tl = await mkt.GetTimelineAsync();
                            var pl = tl.LastOrDefault(s => s is PublishedList) as PublishedList;
                            var p = pl?.Packs.FirstOrDefault(x => x.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
                            if (p != null) code = p.Code;
                        }
                        if (code != null)
                        {
                            var res = await CodeRunner.ExecuteCode(code, inp);
                            Console.WriteLine("Result: " + res);
                        }
                        else
                        {
                            Console.WriteLine("Nothing to run. create-software then run, or run <name>");
                        }
                    }
                    break;

                case "self-improve":
                    {
                        var d = "marketplace pack analyzer that prints 3 ideas to improve the compiler or REPL";
                        var c = grains.GetGrain<ICompiler>("compiler-main");
                        await c.FireAsync(new CreateNeuronRequest(d));
                        await Task.Delay(800);
                        var tl = await c.GetTimelineAsync();
                        var g = tl.LastOrDefault(s => s is NeuronCodeGenerated) as NeuronCodeGenerated;
                        if (g != null)
                        {
                            lastGeneratedCode = g.GeneratedCodeSnippet;
                            lastGeneratedDesc = "SelfAnalyzer";
                            CodeRunner.MaterializeAsProject("SelfAnalyzer", lastGeneratedCode);
                            var r = await CodeRunner.ExecuteCode(lastGeneratedCode);
                            Console.WriteLine("=== analysis ===\n" + r);
                            await grains.GetGrain<INeuron>("optimizer-main").FireAsync(new WiringOptimizationProposed(r ?? "", "self-generated-analyzer"));
                            Console.WriteLine("fed result from generated software to MetaOptimizer");
                            var mk = grains.GetGrain<IMarketplaceNeuron>("market-main");
                            await mk.FireAsync(new PublishToMarketplace("SelfAnalyzer", "0.1", lastGeneratedCode, "self", false, 0.0, d));
                            Console.WriteLine("published SelfAnalyzer");
                        }
                    }
                    break;

                case "ask-llm":
                    if (parts.Length > 1)
                    {
                        var llm = grains.GetGrain<ILlmNeuron>("llm-main");
                        await llm.FireAsync(new LlmPrompt(string.Join(' ', parts[1..])));
                        await Task.Delay(800);
                        var tl = await llm.GetTimelineAsync();
                        var resp = tl.OfType<LlmResponse>().LastOrDefault();
                        Console.WriteLine(resp != null ? resp.Response : "done");
                    }
                    break;

                case "list":
                    var m = grains.GetGrain<IMarketplaceNeuron>("market-main");
                    await m.FireAsync(new ListPublished());
                    var mtl = await m.GetTimelineAsync();
                    var pll = mtl.LastOrDefault(s => s is PublishedList) as PublishedList;
                    if (pll != null)
                        foreach (var p in pll.Packs) Console.WriteLine(p.Name + "@" + p.Version);
                    break;

                case "help":
                    Console.WriteLine("create-software ... | ino 'plan my week using kernel tasks' | task 'backup files' | checkpoint | branch | timelines | list");
                    break;

                case "ino":
                    if (parts.Length > 1)
                    {
                        var q = string.Join(' ', parts[1..]);
                        var ino = grains.GetGrain<IInoNeuron>("ino-main");
                        var resp = await ino.AskAsync(q);
                        Console.WriteLine("INO: " + resp);
                        var itl = await ino.GetOutgoingTimelineAsync();
                        var lastResp = itl.OfType<InoResponse>().LastOrDefault();
                        if (lastResp != null && lastResp.UsedTaskIds.Length > 0)
                            Console.WriteLine("  tasks: " + string.Join(",", lastResp.UsedTaskIds));
                    }
                    break;

                case "task":
                    if (parts.Length > 1)
                    {
                        var desc = string.Join(' ', parts[1..]);
                        var tid = "quick-" + Guid.NewGuid().ToString("N")[..6];
                        var kt = grains.GetGrain<IKernelTask>(tid);
                        await kt.FireAsync(new RunKernelTask(tid, desc));
                        await Task.Delay(100);
                        var info = await kt.GetInfoAsync();
                        Console.WriteLine($"KernelTask {info.TaskId}:{info.Status} result={info.Result}");
                    }
                    break;

                case "checkpoint":
                    {
                        var st = grains.GetGrain<ISystemStatus>("status-main");
                        var cp = await st.CreateCheckpointAsync();
                        Console.WriteLine($"Checkpoint at {cp.TakenAt}: {cp.Snapshot.Count} entries for {cp.Source}");
                    }
                    break;

                case "branch":
                    {
                        var st = grains.GetGrain<ISystemStatus>("status-main");
                        var cp = await st.CreateCheckpointAsync();
                        var bid = await st.BranchAsync(cp);
                        Console.WriteLine("Branched to " + bid.Value);
                        // Time travel demo: interact with branch (separate journals)
                        var branchGrain = grains.GetGrain<INeuron>(bid.Value);
                        await branchGrain.FireAsync(new DemoMessageSynapse("time-travelled on branch"));
                        var btl = await branchGrain.GetOutgoingTimelineAsync();
                        Console.WriteLine("Branch timeline count now: " + btl.Count);
                        var mainTl = await st.GetOutgoingTimelineAsync();
                        Console.WriteLine("Main (no pollution): " + mainTl.Count);
                    }
                    break;

                case "timelines":
                    {
                        var ino = grains.GetGrain<IInoNeuron>("ino-main");
                        var inn = await ino.GetIncomingTimelineAsync();
                        var outt = await ino.GetOutgoingTimelineAsync();
                        Console.WriteLine($"INO in:{inn.Count} out:{outt.Count}");
                    }
                    break;

                default:
                    Console.WriteLine("unknown. try create-software 'word counter' ; ino 'help' ; task 'x' ; branch ; help");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("err: " + ex.Message);
        }
    }

    await app.StopAsync();
}

Console.WriteLine("DigitalBrain shut down.");

internal static class CodeRunner
{
    public static async Task<string> ExecuteCode(string code, string input = "")
    {
        if (string.IsNullOrWhiteSpace(code)) return "No code.";
        try
        {
            var scriptOptions = ScriptOptions.Default
                .AddReferences(typeof(object).Assembly, typeof(Console).Assembly, typeof(Enumerable).Assembly)
                .AddImports("System", "System.Collections.Generic", "System.Linq", "System.IO");

            bool looksLikeClass = code.Contains("class ") || code.Contains("public class");

            if (looksLikeClass)
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var refs = new List<MetadataReference>();
                void TryAdd(Type t) { try { if (!string.IsNullOrEmpty(t.Assembly.Location)) refs.Add(MetadataReference.CreateFromFile(t.Assembly.Location)); } catch { } }
                TryAdd(typeof(object));
                TryAdd(typeof(Console));
                TryAdd(typeof(Enumerable));
                TryAdd(typeof(List<>));
                TryAdd(typeof(System.Text.StringBuilder));
                TryAdd(typeof(System.Linq.Expressions.Expression));

                var compilation = CSharpCompilation.Create(
                        "BrainDynamic",
                        new[] { syntaxTree },
                        refs,
                        new CSharpCompilationOptions(OutputKind.ConsoleApplication));

                using var ms = new MemoryStream();
                var emitResult = compilation.Emit(ms);
                if (!emitResult.Success)
                {
                    var errs = emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage());
                    return "Compile errors: " + string.Join("; ", errs);
                }

                ms.Position = 0;
                var asm = Assembly.Load(ms.ToArray());
                var entry = asm.EntryPoint;
                if (entry != null)
                {
                    var p = entry.GetParameters();
                    var args = p.Length > 0 ? new object?[] { new string[] { input } } : null;
                    var res = entry.Invoke(null, args);
                    return res?.ToString() ?? "Entry ran.";
                }

                var candidate = asm.GetTypes()
                    .FirstOrDefault(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                        .Any(m => m.Name is "Run" or "Main" or "Execute"));
                if (candidate != null)
                {
                    var m = candidate.GetMethod("Run") ?? candidate.GetMethod("Main") ?? candidate.GetMethod("Execute");
                    if (m != null)
                    {
                        var target = m.IsStatic ? null : Activator.CreateInstance(candidate);
                        var ps = m.GetParameters();
                        var callArgs = ps.Length == 0 ? null : new object?[] { input };
                        var invokeRes = m.Invoke(target, callArgs);
                        return invokeRes?.ToString() ?? "Invoked.";
                    }
                }
                return "Compiled, no entry or Run/Main found.";
            }

            var scriptRes = await CSharpScript.EvaluateAsync(code, scriptOptions);
            return scriptRes?.ToString() ?? "Script ran.";
        }
        catch (Exception ex)
        {
            return "Exec error: " + ex.Message;
        }
    }

    public static void MaterializeAsProject(string description, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var safe = new string((description ?? "Automation").Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0) safe = "Automation";
        safe = char.ToUpper(safe[0]) + (safe.Length > 1 ? safe[1..].ToLower() : "");
        var baseDir = Path.Combine("output", safe);
        Directory.CreateDirectory(baseDir);

        string programFileContent = code;
        if (!code.Contains("void Main(") && !code.Contains("static void Main") && code.Contains("class "))
        {
            programFileContent = code + @"
static class __Runner {
    public static void Main(string[] a) {
        var inp = a.Length > 0 ? string.Join("" "", a) : """";
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        foreach (var t in asm.GetTypes()) {
            var m = t.GetMethod(""Run"") ?? t.GetMethod(""Main"");
            if (m != null) {
                var tgt = m.IsStatic ? null : System.Activator.CreateInstance(t);
                var ps = m.GetParameters();
                m.Invoke(tgt, ps.Length > 0 ? new object[] { inp } : null);
                return;
            }
        }
        System.Console.WriteLine(""Generated code loaded (no Run/Main found)."");
    }
}";
        }
        File.WriteAllText(Path.Combine(baseDir, "Program.cs"), programFileContent);

        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
        File.WriteAllText(Path.Combine(baseDir, safe + ".csproj"), csproj);

        var readme = "Generated by DigitalBrain.\n\nRun: dotnet run --project .\nPass args after -- if needed.\n";
        File.WriteAllText(Path.Combine(baseDir, "README.txt"), readme);

        Console.WriteLine($">>> Materialized runnable project to {baseDir} . Use: dotnet run --project {baseDir}");
    }
}

// === MCP TOOLS (integrated for --mcp mode - no separate project needed for "from dust" experience) ===
[McpServerToolType]
public class BrainMcpTools(IGrainFactory grains)
{
    [McpServerTool(Name = "ask_llm_neuron"), Description("Ask the LLM neuron a question. This is how you interact with the brain's intelligence. Use it to generate software ideas, automations, or logic.")]
    public async Task<string> AskLlmNeuron([Description("Your question or prompt, e.g. 'create a simple file backup automation'")] string prompt)
    {
        var llm = grains.GetGrain<ILlmNeuron>("llm-main");
        await llm.FireAsync(new LlmPrompt(prompt));
        await Task.Delay(800);
        var tl = await llm.GetTimelineAsync();
        var r = tl.OfType<LlmResponse>().LastOrDefault();
        return r?.Response ?? "LLM processed the prompt. Use get_timeline on llm-main for full result.";
    }

    [McpServerTool(Name = "generate_software"), Description("Generate real software/automation/logic. Drives compiler, returns code, materializes full runnable project under output/.")]
    public async Task<string> GenerateSoftware([Description("Description of automation or logic to create, e.g. 'daily log rotator that writes timestamped files'")] string description)
    {
        var compiler = grains.GetGrain<ICompiler>("compiler-main");
        await compiler.FireAsync(new CreateNeuronRequest(description));
        await Task.Delay(800);
        var tl = await compiler.GetTimelineAsync();
        var gen = tl.LastOrDefault(s => s is NeuronCodeGenerated) as NeuronCodeGenerated;
        var code = gen?.GeneratedCodeSnippet;
        if (!string.IsNullOrWhiteSpace(code))
        {
            try
            {
                CodeRunner.MaterializeAsProject(description, code);
                return "Generated + materialized. Code:\n" + code;
            }
            catch { }
            return "Generated:\n" + code;
        }
        return "Generation failed. Try more specific description.";
    }

    [McpServerTool(Name = "execute_software"), Description("Execute code or installed pack snippet inside the brain using Roslyn. For end-to-end agent driven runs.")]
    public async Task<string> ExecuteSoftware([Description("The C# code to run (class with Run/Main or script)")] string code, [Description("Optional input arg")] string? input = null)
    {
        var res = await CodeRunner.ExecuteCode(code, input ?? "");
        return "Result: " + res;
    }

    [McpServerTool(Name = "list_marketplace"), Description("List packs available in the persistent marketplace library.")]
    public async Task<string> ListMarketplace()
    {
        var m = grains.GetGrain<IMarketplaceNeuron>("market-main");
        await m.FireAsync(new ListPublished());
        await Task.Delay(100);
        var tl = await m.GetTimelineAsync();
        if (tl.LastOrDefault(s => s is PublishedList) is PublishedList pl && pl.Packs.Count > 0)
            return string.Join("\n", pl.Packs.Select(p => p.Name + "@" + p.Version));
        return "No packs published.";
    }

    [McpServerTool(Name = "publish_to_marketplace"), Description("Publish code as durable pack in marketplace (owner local, low commission).")]
    public async Task<string> PublishPack(string name, string code, string description = "")
    {
        await grains.GetGrain<IMarketplaceNeuron>("market-main")
            .FireAsync(new PublishToMarketplace(name, "0.1-dev", code, "mcp", false, 0.05, description));
        return "Published " + name + " to marketplace library.";
    }

    [McpServerTool(Name = "install_from_marketplace"), Description("Install pack (activates as GeneratedNeuron using the pack code/desc).")]
    public async Task<string> InstallPack(string name, string version = "0.1-dev")
    {
        await grains.GetGrain<IMarketplaceNeuron>("market-main")
            .FireAsync(new InstallFromMarketplace(name, version, "mcp-user"));
        return "Installed " + name + " (embodied for use-generated).";
    }

    [McpServerTool(Name = "run_pack"), Description("Find pack by name (from durable marketplace) and execute its code with Roslyn.")]
    public async Task<string> RunPack(string name, string? input = null)
    {
        var m = grains.GetGrain<IMarketplaceNeuron>("market-main");
        await m.FireAsync(new ListPublished());
        await Task.Delay(100);
        var tl = await m.GetTimelineAsync();
        var pl = tl.LastOrDefault(s => s is PublishedList) as PublishedList;
        var pack = pl?.Packs.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (pack == null) return "Pack not found. Use list_marketplace.";
        var res = await CodeRunner.ExecuteCode(pack.Code, input ?? "");
        return "Result: " + res;
    }

    [McpServerTool(Name = "ask_ino"), Description("Talk to INO (the personal assistant). Uses dual journals + kernel tasks for context and action.")]
    public async Task<string> AskIno([Description("Natural language request to INO e.g. 'plan my tasks and run a backup'")] string prompt)
    {
        var ino = grains.GetGrain<IInoNeuron>("ino-main");
        return await ino.AskAsync(prompt);
    }

    [McpServerTool(Name = "kernel_task_info"), Description("Get status and result of a kernel task by id (self-recoverable unit).")]
    public async Task<string> KernelTaskInfo([Description("The task id returned from previous ino or task commands")] string taskId)
    {
        var kt = grains.GetGrain<IKernelTask>(taskId);
        var info = await kt.GetInfoAsync();
        return $"{info.TaskId} status={info.Status} result={info.Result}";
    }
}