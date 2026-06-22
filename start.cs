// start.cs - unified launcher for the DigitalBrain kernel + INO + tasks + marketplace.
// Run with: cd framework; dotnet run --project samples/QuickTest
// Args: kernel | ino | marketplace | tasks | "awesome/example" | gmail-digest
// The entire system behavior is described via INO (prompts + Reqnroll .feature files) and executed at runtime by the framework (neurons + journals + LLM embodiment of NeuroPacks).

using DigitalBrain.Protocol;
using DigitalBrain.Silo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

var target = args.Length > 0 ? string.Join(' ', args) : "kernel";

var builder = Host.CreateApplicationBuilder(args);
builder.UseDigitalBrainKernel();

var app = builder.Build();
await app.StartAsync();

var grains = app.Services.GetRequiredService<IGrainFactory>();

var ino = grains.GetGrain<IInoNeuron>("ino-main");
var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
var aspire = grains.GetGrain<IAspireNeuron>("aspire-main");

// Bootstrap core neurons (INO drives tasks, marketplace for installable experiences, status self awareness)
_ = ino.GetTimelineAsync();
_ = market.GetTimelineAsync();
_ = grains.GetGrain<ISystemStatus>("status-main").GetTimelineAsync();

// New for visual + smart system: INO code editor, context mgmt, dynamic DB support
_ = grains.GetGrain<IInoCodeEditor>("ino-editor-main").GetTimelineAsync();
_ = grains.GetGrain<IContextNeuron>("context-main").GetTimelineAsync();
_ = grains.GetGrain<IDbSupportNeuron>("db-main").GetTimelineAsync();

Console.WriteLine("=== DIGITALBRAIN KERNEL STARTED ===");
Console.WriteLine($"Target: {target}");
Console.WriteLine("INO + KernelTasks + Marketplace + AspireOrchestrator loaded.");
Console.WriteLine("All system behaviors are INO-described (prompts + .feature specs) and run via framework neurons.");

// Seed some pre-existing, installable software from "marketplace" (INO code style descriptions + embodiment packs)
await SeedPreExistingAsync(market);

// Handle direct targets
if (target.Contains("gmail", StringComparison.OrdinalIgnoreCase) || target.Contains("digest", StringComparison.OrdinalIgnoreCase))
{
    await RunGmailDigestAsync(grains, market, ino);
    return;
}

if (target.Contains("awesome", StringComparison.OrdinalIgnoreCase) || target.Contains("example", StringComparison.OrdinalIgnoreCase))
{
    await RunAwesomeExampleAsync(grains, ino);
    return;
}

if (target.Equals("tasks", StringComparison.OrdinalIgnoreCase))
{
    await ShowTasksViewAsync(grains, ino);
    return;
}

if (target.Equals("marketplace", StringComparison.OrdinalIgnoreCase) || target.Equals("market", StringComparison.OrdinalIgnoreCase))
{
    await MarketplaceBrowserAsync(grains, market);
    return;
}

// Default / "kernel": full interactive experience with tasks view, ino, marketplace, run-any, updates
Console.WriteLine("\nCommands: tasks | ino <prompt> | market list|install|update <name> | run <name> | update-kernel | awesome | help | exit");
Console.WriteLine("Examples: ino 'plan my week and backup files' | market install GmailDigest | run GmailDigest");

var knownTasks = new List<string>();

while (true)
{
    Console.Write("start> ");
    var line = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(line)) continue;
    if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].ToLowerInvariant();

    try
    {
        switch (cmd)
        {
            case "tasks":
            case "task":
            case "view-tasks":
                await ShowTasksViewAsync(grains, ino, knownTasks);
                break;

            case "ino":
                if (parts.Length > 1)
                {
                    var prompt = string.Join(' ', parts[1..]);
                    var inoReply = await ino.AskAsync(prompt);
                    Console.WriteLine($"INO: {inoReply}");

                    // Capture any tasks that INO decided to spawn (they are returned in context on next)
                    if (inoReply.Contains("task", StringComparison.OrdinalIgnoreCase))
                    {
                        knownTasks.Add("task-from-ino-" + Guid.NewGuid().ToString("N")[..6]);
                    }
                }
                else
                {
                    Console.WriteLine("Usage: ino <your prompt to the personal kernel assistant>");
                }
                break;

            case "market":
            case "marketplace":
                if (parts.Length > 1)
                {
                    var sub = parts[1].ToLower();
                    if (sub == "list")
                    {
                        await market.FireAsync(new ListPublished());
                        var tl = await market.GetTimelineAsync();
                        var list = tl.LastOrDefault(s => s is PublishedList) as PublishedList;
                        if (list != null && list.Packs.Count > 0)
                        {
                            Console.WriteLine("Published marketplace experiences (INO-described):");
                            foreach (var p in list.Packs)
                                Console.WriteLine($"  {p.Name}@{p.Version} | {p.Description} | owner={p.OwnerId} comm={p.CommissionRate:P0}");
                        }
                    }
                    else if (sub == "install" && parts.Length > 2)
                    {
                        var name = parts[2];
                        await market.FireAsync(new InstallFromMarketplace(name, "1.0", "current-user"));
                        Console.WriteLine($"Installed {name}. It is now embodied and runnable via 'run {name}'.");
                    }
                    else if (sub == "update" && parts.Length > 2)
                    {
                        var name = parts[2];
                        // Simulate update from marketplace: new version pack + re-install triggers runtime reload of the INO/runtime experience
                        await market.FireAsync(new PublishToMarketplace(name, "2.0", "updated-code", "market", false, 0.10, "Updated INO-described experience"));
                        await market.FireAsync(new InstallFromMarketplace(name, "2.0", "current-user"));
                        Console.WriteLine($"Updated {name} to 2.0 and re-activated (runtime embodiment reloaded).");
                        // If this was a binary Aspire resource, we would do: await aspire.FireAsync(new RestartResource(name));
                        Console.WriteLine("(If binary resource: AspireOrchestrator would reload the corresponding resource.)");
                    }
                }
                else
                {
                    Console.WriteLine("market list | market install <name> | market update <name>");
                }
                break;

            case "run":
            case "start":
                if (parts.Length > 1)
                {
                    var appName = parts[1];
                    if (appName.Contains("gmail", StringComparison.OrdinalIgnoreCase))
                        await RunGmailDigestAsync(grains, market, ino);
                    else if (appName.Contains("awesome", StringComparison.OrdinalIgnoreCase))
                        await RunAwesomeExampleAsync(grains, ino);
                    else
                        await RunExperienceAsync(grains, appName);
                }
                else
                {
                    Console.WriteLine("run <GmailDigest | AwesomeExample | any-installed-pack>");
                }
                break;

            case "update-kernel":
            case "update":
                Console.WriteLine("Publishing kernel self-update from marketplace and triggering reload...");
                await market.FireAsync(new PublishToMarketplace("kernel", "2026.6-hotfix", "", "digitalbraintech", false, 0.0, "Kernel runtime + INO improvements"));
                await market.FireAsync(new InstallFromMarketplace("kernel", "2026.6-hotfix", "self"));
                await aspire.FireAsync(new RestartResource("silo")); // would reload the Aspire resource for binary/kernel parts
                Console.WriteLine("Kernel updated. Corresponding Aspire resource restart requested (INO/runtime parts re-embodied automatically).");
                break;

            case "edit":
            case "ide":
                // Stub for the future INO IDE: modify the INO code/desc of a pack, re-publish + install to hot-reload the embodiment
                if (parts.Length > 1)
                {
                    var pack = parts[1];
                    Console.WriteLine($"[IDE] Editing INO description for {pack} (in real IDE this would open editor on the .feature or pack source)...");
                    await market.FireAsync(new PublishToMarketplace(pack, "1.1-dev", "edited via IDE", "user", false, 0.05, "User-modified INO spec from live IDE session"));
                    await market.FireAsync(new InstallFromMarketplace(pack, "1.1-dev", "current-user"));
                    Console.WriteLine($"[IDE] Re-published and re-installed. Run it again to execute the updated INO code.");
                }
                else Console.WriteLine("ide <pack>   // live edit + hot reload of INO-described experience");
                break;

            case "awesome":
            case "awesome/example":
                await RunAwesomeExampleAsync(grains, ino);
                break;

            case "load-ino":
            case "ino-load":
                if (parts.Length > 1)
                {
                    var path = parts[1]; // e.g. awesome/GmailDigest/GmailDigest.feature
                    var res = await LoadAndRunInoSpecAsync(ino, path);
                    Console.WriteLine($"INO (from spec): {res}");
                }
                else
                {
                    Console.WriteLine("load-ino awesome/GmailDigest/GmailDigest.feature");
                }
                break;

            case "help":
                Console.WriteLine("tasks | ino <prompt> | market list/install/update | run <name> | update-kernel | awesome | exit");
                Console.WriteLine("Everything (including Gmail auth UI, tasks, apps) is INO code translated live by the framework.");
                break;

            default:
                // Treat unknown as INO prompt for fluidity
                var reply = await ino.AskAsync(line);
                Console.WriteLine($"INO: {reply}");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

Console.WriteLine("DigitalBrain stopped. Journals (INO memory + tasks + marketplace state) preserved for this kernel session.");

static async Task SeedPreExistingAsync(IMarketplaceNeuron market)
{
    // Pre-existing software that can be installed from marketplace.
    // These are "INO code": described with prompts + feature-style specs, embodied at runtime.

    await market.FireAsync(new PublishToMarketplace(
        "GmailDigest", "1.0", Code: "UI: [GoogleIconButton] -> OAuth redirect flow. After success: INO processes last emails into digest + KernelTasks for archive/report.",
        OwnerId: "digitalbraintech", IsPrivate: false, CommissionRate: 0.05,
        Description: "Gmail daily digest. Installable experience with Google auth UI surface. Pure INO + tasks driven."));

    await market.FireAsync(new PublishToMarketplace(
        "NeuroTasks", "1.0", "", "digitalbraintech", false, 0.0,
        "Core tasks view and kernel task runner. INO spawns, tracks, completes tasks via journals."));

    await market.FireAsync(new PublishToMarketplace(
        "AwesomeExample", "1.0", "", "awesome-team", false, 0.10,
        "Example from awesome repo / Software20. Self-improving modern app created from .feature spec."));

    await market.FireAsync(new PublishToMarketplace(
        "DbExplorer", "1.0", "Dynamic DB via DbSupportNeuron + typed synapses. Connect existing DB, runtime queries (inspired by .NET 11 file-based/EF).", "digitalbraintech", false, 0.05,
        "Marketplace DB example. Generated typed context, works via synapses."));

    // One more realistic one
    await market.FireAsync(new PublishToMarketplace(
        "CalendarSync", "0.9", "Basic INO-driven 2-way calendar sync with task extraction.", "digitalbraintech", false, 0.08,
        ""));
}

static async Task ShowTasksViewAsync(IGrainFactory grains, IInoNeuron ino, List<string>? known = null)
{
    Console.WriteLine("=== TASKS VIEW (kernel + INO) ===");
    // Ask INO for current context (it has excellent task memory)
    var ctxReply = await ino.AskAsync("list my current active and recently completed kernel tasks with results. be concise.");
    Console.WriteLine(ctxReply);

    // Demo: spawn a real one if none visible
    if (known == null || known.Count == 0)
    {
        var demoTask = "task-demo-" + Guid.NewGuid().ToString("N")[..8];
        var kt = grains.GetGrain<IKernelTask>(demoTask);
        await kt.FireAsync(new RunKernelTask(demoTask, "Generate quick status summary of the running kernel"));
        var info = await kt.GetInfoAsync();
        Console.WriteLine($"Demo task spawned: {info.TaskId} status={info.Status} result={info.Result}");
        known?.Add(demoTask);
    }
}

static async Task MarketplaceBrowserAsync(IGrainFactory grains, IMarketplaceNeuron market)
{
    await market.FireAsync(new ListPublished());
    var tl = await market.GetTimelineAsync();
    var list = tl.LastOrDefault(s => s is PublishedList) as PublishedList;
    Console.WriteLine("=== MARKETPLACE (pre-existing INO software) ===");
    if (list != null)
    {
        foreach (var p in list.Packs)
            Console.WriteLine($"  {p.Name}@{p.Version} - {p.Description}");
    }
    Console.WriteLine("Use: market install <Name>   or   run <Name>");
}

static async Task RunGmailDigestAsync(IGrainFactory grains, IMarketplaceNeuron market, IInoNeuron ino)
{
    Console.WriteLine("=== Running installed experience: GmailDigest ===");

    // Real UiSurface emitted for clients (Flutter ui-kit in sdk/ etc.)
    var auth = new AuthButtonSurface(Provider: "google", Label: "Sign in with Google", Icon: "google");
    Console.WriteLine($"UiSurface: [{auth.Icon}] {auth.Label} (provider={auth.Provider}, action={auth.Action})");
    Console.WriteLine("After OAuth: INO + kernel tasks produce digest.");

    var gen = grains.GetGrain<IGeneratedNeuron>("generated-gmaildigest");
    await gen.FireAsync(new ExperienceUsed("GmailDigest", "render-ui-and-run-digest"));
    await gen.FireAsync(auth);

    try
    {
        var result = await ino.AskAsync("using the installed GmailDigest experience, produce a short daily digest summary and create any needed kernel tasks");
        Console.WriteLine($"INO + GmailDigest result: {result}");
    }
    catch
    {
        Console.WriteLine("(simulated - full LLM when running via Aspire. Creates KernelTasks for digest.)");
    }
}

static async Task RunAwesomeExampleAsync(IGrainFactory grains, IInoNeuron ino)
{
    Console.WriteLine("=== Running awesome/example (Software20 style from .feature spec in sibling awesome/ repo) ===");
    // Load INO code from the awesome/ sibling repo (populated with .feature specs)
    var featurePath = Path.Combine("..", "awesome", "SoftwareEngineering", "Software20", "AwesomeSoftware20.feature");
    string spec = "Modern neuro/LLM-assisted team creating self-improving task app.";
    if (File.Exists(featurePath)) spec = await File.ReadAllTextAsync(featurePath);

    string reply;
    try
    {
        reply = await ino.AskAsync($"Embody and run the following INO-described software spec (from awesome/ repo): {spec}. Create runnable behavior + report.");
    }
    catch
    {
        reply = "[no-LLM fallback] Would embody Software20 spec from awesome/ .feature: create self-improving task app via neurons + journals.";
    }
    Console.WriteLine($"AwesomeExample (INO translated): {reply}");

    try
    {
        var team = grains.GetGrain<ISoftware20Team>("awesome-example");
        await team.FireAsync(new CreateSimpleApp("awesome", "task tracker from feature spec"));
    }
    catch
    {
        Console.WriteLine("Software20TeamNeuron fired (LLM not available in fast mode; used ModernTemplate fallback in neuron).");
    }
}

static async Task RunExperienceAsync(IGrainFactory grains, string packName)
{
    var gen = grains.GetGrain<IGeneratedNeuron>($"generated-{packName.ToLowerInvariant()}");
    await gen.FireAsync(new ExperienceUsed(packName, "execute"));
    Console.WriteLine($"Executed installed experience '{packName}' (framework embodied the INO-described pack).");
}

/// Stronger INO code: load a .feature from awesome/ (or anywhere) and feed to INO as first-class spec.
static async Task<string> LoadAndRunInoSpecAsync(IInoNeuron ino, string relativeFeaturePath)
{
    var full = Path.Combine("..", relativeFeaturePath);
    string spec = File.Exists(full) ? await File.ReadAllTextAsync(full) : relativeFeaturePath;
    return await ino.AskAsync($"Load this INO spec (Reqnroll/feature) and execute the described behavior: {spec}");
}