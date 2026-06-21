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
using System.ComponentModel;
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
    Console.WriteLine("Kernel + client + REPL ready. Marketplace, LLM neuron, everything is alive.");
    Console.WriteLine("Commands: generate <desc> | export | publish ... | install | ask-llm <prompt> | use-generated <pack> <input> | timeline <id> | list | exit");
    Console.WriteLine("Create real software: 'generate simple todo cli automation with file persistence' then 'export' (writes .cs you can run).");
    Console.WriteLine("Self-improving example: generate something useful for the brain (e.g. a pack analyzer), export, then use the ideas to improve prompts or add new tools.");

    string? lastGeneratedCode = null;
    string? lastGeneratedDesc = null;

    async Task<string> ExecuteGeneratedCode(string code, string input = "")
    {
        try
        {
            // Use Roslyn Scripting for simple execution of generated automations/logic.
            // For class with Run(), we wrap it.
            var options = ScriptOptions.Default
                .AddReferences(typeof(object).Assembly, typeof(Console).Assembly, typeof(Enumerable).Assembly)
                .AddImports("System", "System.Collections.Generic", "System.Linq");

            // If it's a class definition, try to find and call a Run method or Main.
            if (code.Contains("public class") || code.Contains("class "))
            {
                // Compile to assembly for full class support.
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var compilation = CSharpCompilation.Create("DynamicAssembly")
                    .AddReferences(
                        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                    .AddSyntaxTrees(syntaxTree);

                using var ms = new System.IO.MemoryStream();
                var emitResult = compilation.Emit(ms);
                if (!emitResult.Success)
                {
                    return "Compilation errors: " + string.Join(", ", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage()));
                }

                ms.Seek(0, System.IO.SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                var type = assembly.GetTypes().FirstOrDefault(t => t.Name.Contains("Automation") || t.Name.Contains("Program") || t.GetMethods().Any(m => m.Name == "Run" || m.Name == "Main"));
                if (type != null)
                {
                    var instance = Activator.CreateInstance(type);
                    var runMethod = type.GetMethod("Run") ?? type.GetMethod("Main");
                    if (runMethod != null)
                    {
                        var invokeResult = runMethod.Invoke(instance, runMethod.GetParameters().Length > 0 ? new object[] { input } : null);
                        return invokeResult?.ToString() ?? "Executed successfully.";
                    }
                }
                return "Compiled but no Run/Main found. Code loaded.";
            }

            // Simple script
            var scriptResult = await CSharpScript.EvaluateAsync(code + (string.IsNullOrEmpty(input) ? "" : $"; Console.WriteLine({input});"), options);
            return scriptResult?.ToString() ?? "Executed.";
        }
        catch (Exception ex)
        {
            return "Execution error: " + ex.Message;
        }
    }

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

                            // Immediately materialize as real file (core to using the system for software creation)
                            var name = lastGeneratedDesc.Replace(" ", "") + "Automation";
                            var dir = "output";
                            Directory.CreateDirectory(dir);
                            var path = Path.Combine(dir, name + ".cs");
                            File.WriteAllText(path, lastGeneratedCode);

                            // Better materialization: also emit a minimal .csproj for standalone project
                            var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>
</Project>";
                            File.WriteAllText(Path.Combine(dir, name + ".csproj"), csprojContent);
                            Console.WriteLine($"\n>>> Exported real software to {path} and {name}.csproj . Run 'dotnet run --project {dir}/{name}.csproj' for the automation.");
                            Console.WriteLine("This is real usable C# software/automation created by the brain.");
                        }
                    }
                    break;

                case "export":
                    if (lastGeneratedCode != null)
                    {
                        var name = lastGeneratedDesc?.Replace(" ", "") ?? "GeneratedAutomation";
                        var dir = "output";
                        Directory.CreateDirectory(dir);
                        var path = Path.Combine(dir, name + ".cs");
                        File.WriteAllText(path, lastGeneratedCode);
                        Console.WriteLine($"Exported to {path}. This is real usable C# software/automation you can compile and run.");
                    }
                    else
                    {
                        Console.WriteLine("Generate something first (use 'generate simple email filter automation')");
                    }
                    break;

                case "run":
                case "execute":
                    if (lastGeneratedCode != null)
                    {
                        var result = await ExecuteGeneratedCode(lastGeneratedCode, parts.Length > 1 ? string.Join(' ', parts[1..]) : "");
                        Console.WriteLine("Execution result: " + result);
                    }
                    else if (parts.Length > 1)
                    {
                        var packName = parts[1];
                        var marketGrain = grains.GetGrain<IMarketplaceNeuron>("market-main");
                        await marketGrain.FireAsync(new ListPublished());
                        var marketTl = await marketGrain.GetTimelineAsync();
                        var publishedList = marketTl.LastOrDefault(s => s is PublishedList) as PublishedList;
                        var targetPack = publishedList?.Packs.FirstOrDefault(p => p.Name.Contains(packName, StringComparison.OrdinalIgnoreCase));
                        if (targetPack != null)
                        {
                            var execResult = await ExecuteGeneratedCode(targetPack.Code, string.Join(' ', parts.Skip(2)));
                            Console.WriteLine("Execution result: " + execResult);
                        }
                    }
                    else
                    {
                        Console.WriteLine("No code to execute. Use 'generate' or 'create-software' first.");
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

                default:
                    Console.WriteLine("unknown command. try 'ask-llm hello' or 'help'");
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

    [McpServerTool(Name = "generate_software"), Description("Generate real software, automation or logic using the compiler. Returns the code + auto-materializes a .cs file in ./output when possible.")]
    public async Task<string> GenerateSoftware([Description("Clear description of the desired software/automation/logic")] string description)
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
                var name = "Generated" + description.Replace(" ", "").Substring(0, Math.Min(20, description.Length));
                var dir = "output";
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, name + ".cs");
                File.WriteAllText(path, code);
                return $"Generated and materialized to {path}:\n\n" + code;
            }
            catch { }
            return "Generated code:\n" + code + "\n(Export to file failed in this environment)";
        }
        return "Generation failed. Try a more specific description.";
    }

    [McpServerTool(Name = "fire_to_neuron"), Description("Fire a message to any neuron.")]
    public async Task<string> FireToNeuron(string neuronId, string text)
    {
        await grains.GetGrain<INeuron>(neuronId).FireAsync(new DemoMessageSynapse(text));
        return "fired";
    }

    // Publish, install, get_timeline, list etc. from earlier versions can be re-added as needed.
    // The generate_software + existing publish/install flow lets agents create, share and embody new software capabilities.
}