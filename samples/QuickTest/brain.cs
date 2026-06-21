// FASTEST "from dust to usable" entry point (following Elon's 5 Steps)
// Command: dotnet run --project samples/QuickTest
// 
// This single entry boots the full DigitalBrain:
// - Kernel (Orleans host with all built-in neurons: Marketplace, Compiler, Llm, SystemStatus...)
// - Client
// - Interactive REPL for human testing ("send text", test private packs + commissions)
// - --mcp flag: runs as MCP server (for LLM agents to ask_llm_neuron etc.)
//
// No Aspire required for this fast path. Use the AppHost for full distributed demos.
//
// Encapsulation: All config is in UseDigitalBrainKernel() + AddDigitalBrainClient() (see DigitalBrain.Silo/DigitalBrainKernelExtensions.cs)
//
// To "dotnet run brain.cs" feeling: copy this file to a temp folder with a minimal .csproj that references DigitalBrain.Silo + ModelContextProtocol + Orleans packages, then dotnet run brain.cs (or rename Program.cs).

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

// Bootstrap key neurons (like the old Silo Program did)
_ = grains.GetGrain<ISystemStatus>("status-main").GetTimelineAsync();

if (isMcpMode)
{
    Console.Error.WriteLine("DigitalBrain MCP server ready. Cluster (kernel) is live.");
    await app.WaitForShutdownAsync();
}
else
{
    Console.WriteLine("=== DIGITALBRAIN BOOTED (from single command) ===");
    Console.WriteLine("Kernel + REPL + Marketplace ready. Type 'help' for flows.");
    Console.WriteLine("Example: create-software 'count lines in cs files'  |  run  |  export  |  self-improve");

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
                case "fire":
                    if (parts.Length > 2)
                    {
                        await grains.GetGrain<INeuron>(parts[1]).FireAsync(new DemoMessageSynapse(parts[2]));
                        Console.WriteLine("fired");
                    }
                    break;

                case "publish":
                    bool priv = parts.Length > 1 && parts[1].Equals("private", StringComparison.OrdinalIgnoreCase);
                    int idx = priv ? 2 : 1;
                    if (parts.Length > idx + 1)
                    {
                        var name = parts[idx];
                        var ver = parts[idx + 1];
                        var code = parts.Length > idx + 2 ? parts[^1] : "// generated";
                        await grains.GetGrain<IMarketplaceNeuron>("market-main")
                            .FireAsync(new PublishToMarketplace(name, ver, code, "brain-user", priv, 0.15));
                        Console.WriteLine($"published {(priv ? "PRIVATE " : "")}{name}@{ver} (15% commission)");
                    }
                    break;

                case "install":
                    if (parts.Length > 1)
                    {
                        await grains.GetGrain<IMarketplaceNeuron>("market-main")
                            .FireAsync(new InstallFromMarketplace(parts[1], "0.1-dev", "brain-buyer"));
                        Console.WriteLine("installed + commission taken. The generated grain now EMBODIES the pack.");
                    }
                    break;

                case "use-generated":
                    if (parts.Length > 2)
                    {
                        var gen = grains.GetGrain<IGeneratedNeuron>("generated-" + parts[1].ToLower());
                        await gen.FireAsync(new ExperienceUsed(parts[1], parts[2]));
                        Console.WriteLine($"Used installed pack '{parts[1]}' with input '{parts[2]}' (behavior now comes from the pack)");
                    }
                    break;

                case "generate":
                case "create":
                    if (parts.Length > 1)
                    {
                        var desc = string.Join(' ', parts[1..]);
                        var compiler = grains.GetGrain<ICompiler>("compiler-main");
                        await compiler.FireAsync(new CreateNeuronRequest(desc));
                        var tl = await compiler.GetTimelineAsync();
                        var genEvt = tl.LastOrDefault(s => s is NeuronCodeGenerated) as NeuronCodeGenerated;
                        if (genEvt != null)
                        {
                            Console.WriteLine("Generated code:\n" + genEvt.GeneratedCodeSnippet);
                            lastGeneratedCode = genEvt.GeneratedCodeSnippet;
                            lastGeneratedDesc = desc;
                        }
                    }
                    break;

                case "create-software":
                case "make-automation":
                    if (parts.Length > 1)
                    {
                        var desc = "Create a simple, complete, runnable C# console automation or logic for: " + string.Join(' ', parts[1..]);
                        var compiler = grains.GetGrain<ICompiler>("compiler-main");
                        await compiler.FireAsync(new CreateNeuronRequest(desc));
                        var tl = await compiler.GetTimelineAsync();
                        var genEvt = tl.LastOrDefault(s => s is NeuronCodeGenerated) as NeuronCodeGenerated;
                        if (genEvt != null)
                        {
                            lastGeneratedCode = genEvt.GeneratedCodeSnippet;
                            lastGeneratedDesc = parts[1];
                            Console.WriteLine("Generated software/automation:\n" + lastGeneratedCode);
                            CodeRunner.MaterializeAsProject(lastGeneratedDesc, lastGeneratedCode);

                            // Auto-publish so it's immediately in the durable library and runnable by name
                            var safe = lastGeneratedDesc.Replace(" ", "").Replace("\"", "").Replace("-", "");
                            await grains.GetGrain<IMarketplaceNeuron>("market-main")
                                .FireAsync(new PublishToMarketplace(safe, "0.1-dev", lastGeneratedCode, "local", false, 0.0, lastGeneratedDesc));
                            Console.WriteLine("Auto-published as " + safe + " (run " + safe + " or list)");
                        }
                    }
                    break;

                case "export":
                    if (lastGeneratedCode != null)
                    {
                        CodeRunner.MaterializeAsProject(lastGeneratedDesc ?? "Generated", lastGeneratedCode);
                    }
                    else
                    {
                        Console.WriteLine("Generate something first (use 'create-software simple email filter automation')");
                    }
                    break;

                case "run":
                case "execute":
                    {
                        string? codeToRun = null;
                        string input = "";
                        if (parts.Length > 1)
                        {
                            var name = parts[1];
                            input = parts.Length > 2 ? string.Join(' ', parts[2..]) : "";
                            var marketGrain = grains.GetGrain<IMarketplaceNeuron>("market-main");
                            await marketGrain.FireAsync(new ListPublished());
                            var marketTl = await marketGrain.GetTimelineAsync();
                            var publishedList = marketTl.LastOrDefault(s => s is PublishedList) as PublishedList;
                            var target = publishedList?.Packs.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                            if (target != null) codeToRun = target.Code;
                        }
                        if (codeToRun == null && lastGeneratedCode != null)
                        {
                            codeToRun = lastGeneratedCode;
                            input = parts.Length > 1 ? string.Join(' ', parts[1..]) : "";
                        }
                        if (codeToRun != null)
                        {
                            var result = await CodeRunner.ExecuteCode(codeToRun, input);
                            Console.WriteLine("Execution result: " + result);
                        }
                        else
                        {
                            Console.WriteLine("Nothing to run. Use 'create-software <desc>' then 'run', or 'run <packname>'.");
                        }
                    }
                    break;

                case "self-improve":
                    {
                        var analyzerDesc = "a marketplace pack analyzer: when run, print 3 actionable ideas to improve the compiler prompt library or add usage logging to the REPL for self-improvement";
                        Console.WriteLine("Generating self-improvement automation: " + analyzerDesc);
                        var comp = grains.GetGrain<ICompiler>("compiler-main");
                        await comp.FireAsync(new CreateNeuronRequest(analyzerDesc));
                        await Task.Delay(2500);
                        var tline = await comp.GetTimelineAsync();
                        var genEvt2 = tline.LastOrDefault(s => s is NeuronCodeGenerated) as NeuronCodeGenerated;
                        if (genEvt2 != null)
                        {
                            lastGeneratedCode = genEvt2.GeneratedCodeSnippet;
                            lastGeneratedDesc = "SelfAnalyzer";
                            CodeRunner.MaterializeAsProject(lastGeneratedDesc, lastGeneratedCode);
                            var analysis = await CodeRunner.ExecuteCode(lastGeneratedCode, "");
                            Console.WriteLine("=== Self-improvement artifact executed ===");
                            Console.WriteLine(analysis);
                            Console.WriteLine(">>> Result fed back. Use ideas above to refine future generations (e.g. publish this analyzer or paste suggestions into next create-software).");
                            var mkt = grains.GetGrain<IMarketplaceNeuron>("market-main");
                            await mkt.FireAsync(new PublishToMarketplace("SelfAnalyzer", "0.1-loop", lastGeneratedCode, "self-brain", false, 0.05, analyzerDesc));
                            Console.WriteLine("Analyzer published to marketplace (survives restart via disk).");
                        }
                    }
                    break;

                case "ask-llm":
                    if (parts.Length > 1)
                    {
                        var llm = grains.GetGrain<ILlmNeuron>("llm-main");
                        await llm.FireAsync(new LlmPrompt(string.Join(' ', parts[1..]), "qwen2.5-coder:1.5b"));
                        await Task.Delay(2000);
                        var tl = await llm.GetTimelineAsync();
                        var resp = tl.OfType<LlmResponse>().LastOrDefault();
                        Console.WriteLine(resp != null ? resp.Response : "LLM fired. Check timeline.");
                    }
                    break;

                case "timeline":
                    if (parts.Length > 1)
                    {
                        var tl = await grains.GetGrain<INeuron>(parts[1]).GetTimelineAsync();
                        foreach (var s in tl.TakeLast(5)) Console.WriteLine($"{s.Type}");
                    }
                    break;

                case "list":
                    var m = grains.GetGrain<IMarketplaceNeuron>("market-main");
                    await m.FireAsync(new ListPublished());
                    var mtl = await m.GetTimelineAsync();
                    if (mtl.LastOrDefault(s => s is PublishedList) is PublishedList pl)
                    {
                        foreach (var p in pl.Packs)
                            Console.WriteLine($"- {p.Name}@{p.Version} private={p.IsPrivate} comm={p.CommissionRate:P0}");
                    }
                    break;

                case "help":
                    Console.WriteLine("Flows: create-software 'desc'  ->  run  ->  export  ->  dotnet run the project in output/");
                    Console.WriteLine("         self-improve (generates + runs + publishes a helper back to marketplace)");
                    Console.WriteLine("         list | run <name> | publish | install <name> | ask-llm <p>");
                    break;

                default:
                    Console.WriteLine("unknown. try: create-software 'word counter' ; run ; help ; exit");
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

        bool hasEntry = code.Contains("void Main(") || code.Contains("static void Main") || code.Contains("int Main(") || !code.Contains("class ");
        string programFileContent;
        if (hasEntry)
        {
            programFileContent = code;
        }
        else
        {
            programFileContent = code + "\n\n" + @"static class __Entry
{
    public static void Main(string[] args)
    {
        var inp = args.Length > 0 ? string.Join("" "", args) : """";
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var methods = asm.GetTypes()
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance))
            .Where(m => m.Name is ""Run"" or ""Main"" or ""Execute"")
            .OrderBy(m => m.Name == ""Run"" ? 0 : 1);
        foreach (var m in methods)
        {
            try
            {
                var tgt = m.IsStatic ? null : Activator.CreateInstance(m.DeclaringType!);
                var p = m.GetParameters();
                object? r = p.Length == 0 ? m.Invoke(tgt, null) : m.Invoke(tgt, new object?[] { inp });
                if (r != null) Console.WriteLine(r);
                return;
            }
            catch { }
        }
        Console.WriteLine(""Automation ready (no Run/Main auto-detected)."");
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
        await Task.Delay(2500);
        var tl = await llm.GetTimelineAsync();
        var r = tl.OfType<LlmResponse>().LastOrDefault();
        return r?.Response ?? "LLM processed the prompt. Use get_timeline on llm-main for full result.";
    }

    [McpServerTool(Name = "generate_software"), Description("Generate real software/automation/logic. Drives compiler, returns code, materializes full runnable project under output/.")]
    public async Task<string> GenerateSoftware([Description("Description of automation or logic to create, e.g. 'daily log rotator that writes timestamped files'")] string description)
    {
        var compiler = grains.GetGrain<ICompiler>("compiler-main");
        await compiler.FireAsync(new CreateNeuronRequest(description));
        await Task.Delay(3000);
        var tl = await compiler.GetTimelineAsync();
        var gen = tl.LastOrDefault(s => s is NeuronCodeGenerated) as NeuronCodeGenerated;
        var code = gen?.GeneratedCodeSnippet;
        if (!string.IsNullOrWhiteSpace(code))
        {
            try
            {
                CodeRunner.MaterializeAsProject(description, code);
                var safe = (description.Replace(" ", "").Replace("\"", "").Replace("-", "") + "Auto").Substring(0, Math.Min(20, description.Length + 4));
                await grains.GetGrain<IMarketplaceNeuron>("market-main")
                    .FireAsync(new PublishToMarketplace(safe, "0.1-dev", code, "mcp", false, 0.05, description));
                return "Generated + materialized + published as " + safe + ". Code:\n" + code;
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

    [McpServerTool(Name = "fire_to_neuron"), Description("Fire a message to any neuron.")]
    public async Task<string> FireToNeuron(string neuronId, string text)
    {
        await grains.GetGrain<INeuron>(neuronId).FireAsync(new DemoMessageSynapse(text));
        return "fired";
    }

    [McpServerTool(Name = "list_marketplace"), Description("List packs available in the persistent marketplace library.")]
    public async Task<string> ListMarketplace()
    {
        var m = grains.GetGrain<IMarketplaceNeuron>("market-main");
        await m.FireAsync(new ListPublished());
        await Task.Delay(300);
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
}