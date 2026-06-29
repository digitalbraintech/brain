// GrokCLI TUI - interactive Text User Interface for DigitalBrain (MCP/CLI proxy to neurons)
using DigitalBrain.Cli.Commands;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

if (args.Length > 0 && args[0].Equals("author", StringComparison.OrdinalIgnoreCase))
{
    var file = args.Length > 1 ? args[1] : "";
    var watch = args.Contains("--watch");
    var gatewayIndex = Array.IndexOf(args, "--gateway");
    var gateway = gatewayIndex >= 0 && gatewayIndex + 1 < args.Length
        ? args[gatewayIndex + 1]
        : Environment.GetEnvironmentVariable("DIGITALBRAIN_GATEWAY") ?? "https://localhost:7080";
    return await AuthorCommand.RunAsync(file, watch, gateway);
}

var host = Host.CreateApplicationBuilder(args);

host.AddKeyedRedisClient("redis");
host.UseOrleansClient();

using var app = host.Build();

IGrainFactory? grains = null;
string lastCreatedPack = null;
try
{
    await app.StartAsync();
    grains = app.Services.GetRequiredService<IGrainFactory>();
    AnsiConsole.MarkupLine("[green]DigitalBrain GrokCLI v2 TUI - connected to brain[/]");
}
catch
{
    AnsiConsole.MarkupLine("[yellow]DigitalBrain GrokCLI v2 TUI - standalone demo mode (no brain cluster)[/]");
}

if (!AnsiConsole.Profile.Capabilities.Interactive)
{
    AnsiConsole.MarkupLine("[grey]Running in non-interactive mode (e.g. Aspire resource). Keeping process alive for orchestration.[/]");
    while (true)
    {
        await Task.Delay(10000);
    }
}

while (true)
{

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select an [green]action[/]:")
            .AddChoices(
                "Create Neuron (grok create-neuron)",
                "Publish Experience to Marketplace",
                "List Published Packs",
                "Download/Install Experience",
                "Use Installed Experience",
                "List Installed Packs",
                "Fire Test Synapse",
                "View Neuron Timeline",
                "Trigger Optimize",
                "Exit"));

    if (choice == "Exit") break;

    try
    {
        switch (choice)
        {
            case "Create Neuron (grok create-neuron)":
                var desc = AnsiConsole.Prompt(new TextPrompt<string>("Enter neuron description: ").DefaultValue("analyze emails with chart"));
                AnsiConsole.MarkupLine($"[cyan]Firing CreateNeuronRequest to CompilerNeuron: {desc}[/]");

                if (grains != null)
                {
                    var compiler = grains.GetGrain<ICompiler>("compiler-main");
                    await compiler.FireAsync(new CreateNeuronRequest(desc));

                    var timeline = await compiler.GetTimelineAsync();
                    var generated = timeline.LastOrDefault(s => s.Type == nameof(NeuronCodeGenerated)) as NeuronCodeGenerated;
                    var realPack = timeline.OfType<NeuroPack>().LastOrDefault();
                    if (generated != null)
                    {
                        AnsiConsole.MarkupLine("[green]Generated code:[/]");
                        AnsiConsole.WriteLine(generated.GeneratedCodeSnippet);
                    }
                    if (realPack != null)
                    {
                        lastCreatedPack = $"{realPack.Name}@{realPack.Version}";
                        AnsiConsole.MarkupLine($"[green]Real NeuroPack created: {lastCreatedPack} owner={realPack.OwnerId} commission={realPack.CommissionRate:P0}[/]");
                    }
                    else
                    {
                        lastCreatedPack = "Generated-" + desc.Replace(" ", "").Replace("\"", "").Substring(0, Math.Min(20, desc.Length));
                        AnsiConsole.MarkupLine($"[green]New experience created as pack: {lastCreatedPack}. Now use 'Publish Experience to Marketplace' to share it.[/]");
                    }
                }
                else
                {
                    lastCreatedPack = "Generated-" + desc.Replace(" ", "").Replace("\"", "").Substring(0, Math.Min(20, desc.Length));
                    AnsiConsole.MarkupLine($"[yellow]Simulating create for '{desc}' as pack {lastCreatedPack}. Use publish/install in TUI.[/]");
                }
                break;

            case "Publish Experience to Marketplace":
                var pubPack = lastCreatedPack ?? AnsiConsole.Prompt(new TextPrompt<string>("Pack name to publish (e.g. Generated-...): "));
                var pubVer = AnsiConsole.Prompt(new TextPrompt<string>("Version: ").DefaultValue("0.1-dev"));
                if (grains != null)
                {
                    var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-main");
                    // Pass real pack details to enable private + commissions in marketplace
                    string owner = "grok-user"; // In real would come from auth
                    double commission = 0.15;
                    await marketplace.FireAsync(new PublishToMarketplace(pubPack, pubVer, Code: "", OwnerId: owner, IsPrivate: false, CommissionRate: commission));
                    AnsiConsole.MarkupLine($"[green]Published {pubPack}@{pubVer} (owner={owner}, commission={commission:P0}) to REAL Marketplace.[/]");
                    AnsiConsole.MarkupLine("[green]Packs are now persisted. Friends can install & marketplace takes commission.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Sim: Published {pubPack}@{pubVer} to Marketplace[/]");
                }
                break;

            case "List Published Packs":
                if (grains != null)
                {
                    var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-main");
                    await marketplace.FireAsync(new ListPublished());
                    var mktTimeline = await marketplace.GetTimelineAsync();
                    var list = mktTimeline.LastOrDefault(s => s.Type == nameof(PublishedList)) as PublishedList;
                    if (list != null && list.Packs.Count > 0)
                    {
                        AnsiConsole.MarkupLine("[green]Published packs (REAL - persisted with owner/commission):[/]");
                        foreach (var p in list.Packs)
                        {
                            string priv = p.IsPrivate ? "PRIVATE" : "public";
                            AnsiConsole.WriteLine($"  - {p.Name}@{p.Version} | owner={p.OwnerId} | {priv} | commission={p.CommissionRate:P0}");
                            if (!string.IsNullOrWhiteSpace(p.Description)) AnsiConsole.WriteLine($"      desc: {p.Description}");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]No published packs. Create one and Publish![/]");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Sim published: " + (lastCreatedPack ?? "Generated-...") + "@0.1-dev[/]");
                }
                break;

            case "Download/Install Experience":
                var instPack = AnsiConsole.Prompt(new TextPrompt<string>("Pack name to download/install (from published): "));
                var instVer = AnsiConsole.Prompt(new TextPrompt<string>("Version: ").DefaultValue("0.1-dev"));
                if (grains != null)
                {
                    var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-main");
                    string buyer = "friend-user"; // real auth would provide this
                    await marketplace.FireAsync(new InstallFromMarketplace(instPack, instVer, BuyerId: buyer));
                    AnsiConsole.MarkupLine($"[green]Downloaded and installed {instPack}@{instVer} as {buyer}.[/]");
                    AnsiConsole.MarkupLine("[green]Commission taken by marketplace. Experience activated in GeneratedNeuron.[/]");
                    AnsiConsole.MarkupLine("[green]Use 'Use Installed Experience' to interact with the real pack code.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Sim: Downloaded/installed {instPack}@{instVer} and activated[/]");
                }
                break;

            case "Use Installed Experience":
                var usePack = AnsiConsole.Prompt(new TextPrompt<string>("Pack name to use (e.g. Generated-...): "));
                var useAction = AnsiConsole.Prompt(new TextPrompt<string>("Action: ").DefaultValue("run"));
                if (grains != null)
                {
                    var genKey = "generated-" + usePack.ToLower();
                    var genNeuron = grains.GetGrain<IGeneratedNeuron>(genKey);
                    await genNeuron.FireAsync(new ExperienceUsed(usePack, useAction));
                    AnsiConsole.MarkupLine($"[green]Used {usePack} : {useAction} (fired to GeneratedNeuron - downloader can now interact)[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Sim: Used experience {usePack} : {useAction}[/]");
                }
                break;

            case "List Installed Packs":
                if (grains != null)
                {
                    var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-main");
                    await marketplace.FireAsync(new ListPublished());
                    var mktTimeline = await marketplace.GetTimelineAsync();
                    var list = mktTimeline.LastOrDefault(s => s.Type == nameof(PublishedList)) as PublishedList;
                    if (list != null)
                    {
                        AnsiConsole.MarkupLine("[green]Installed/available packs (from real marketplace state):[/]");
                        foreach (var p in list.Packs)
                        {
                            string priv = p.IsPrivate ? "PRIVATE" : "public";
                            AnsiConsole.WriteLine($"  - {p.Name}@{p.Version} | owner={p.OwnerId} | {priv} | comm={p.CommissionRate:P0}");
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Sim: EmailVisualizer@1.0, Generated-...@0.1-dev[/]");
                }
                break;

            case "Fire Test Synapse":
                var neuronId = AnsiConsole.Prompt(new TextPrompt<string>("Neuron ID (e.g. demo-opt): ").DefaultValue("demo-opt"));
                var text = AnsiConsole.Prompt(new TextPrompt<string>("Message text: ").DefaultValue("test from TUI"));
                if (grains != null)
                {
                    var neuron = grains.GetGrain<INeuron>(neuronId);
                    await neuron.FireAsync(new DemoMessageSynapse(text));
                    AnsiConsole.MarkupLine("[green]Fired DemoMessageSynapse[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Sim: Fired to " + neuronId + "[/]");
                }
                break;

            case "View Neuron Timeline":
                var viewId = AnsiConsole.Prompt(new TextPrompt<string>("Neuron ID: ").DefaultValue("compiler-main"));
                if (grains != null)
                {
                    var neuron = grains.GetGrain<INeuron>(viewId);
                    var tl = await neuron.GetTimelineAsync();
                    AnsiConsole.MarkupLine($"[green]Timeline for {viewId} ({tl.Count} entries):[/]");
                    foreach (var s in tl.TakeLast(5)) AnsiConsole.WriteLine($"  {s.Type} @ {s.Timestamp}");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Sim: showing sample timeline[/]");
                }
                break;

            case "Trigger Optimize":
                if (grains != null)
                {
                    var opt = grains.GetGrain<IMetaOptimizerNeuron>("optimizer1");
                    await opt.FireAsync(new NeuronTelemetry(new NeuronId("tui"), "manual-trigger", 10));
                    AnsiConsole.MarkupLine("[green]Triggered optimizer telemetry[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Sim: optimizer would propose wiring improvements[/]");
                }
                break;
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
    }

    AnsiConsole.WriteLine();
}

if (grains != null)
{
    await app.StopAsync();
}

// === FASTEST WAY TO TEST (Elon: accelerate cycle time, delete menu friction) ===
// After 'aspire run' (local marketplace) or 'dotnet run' on this cli:
// drops into live REPL connected to real cluster.
// Send text, test private marketplace + commissions immediately.
// Use with MCP for self-inspection / runtime updates.
if (grains != null)
{
    AnsiConsole.MarkupLine("\n[bold green]LIVE REPL - real cluster + marketplace. 'help' or 'exit'[/]");
    while (true)
    {
        var line = AnsiConsole.Ask<string>("[cyan]live>[/]");
        if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
        if (line.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.WriteLine("fire <neuronId> <text> | publish private? <name> <ver> <code> | install <name> [buyer=xx] | timeline <id> | list | exit");
            continue;
        }
        try
        {
            var t = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (t[0] == "fire" && t.Length > 2)
                await grains.GetGrain<INeuron>(t[1]).FireAsync(new DemoMessageSynapse(t[2]));
            else if (t[0] == "publish")
            {
                bool priv = t.Length > 1 && t[1] == "private";
                // simple parse for demo
                await grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new PublishToMarketplace("LiveTestPack", "1.0", line, "live", priv, 0.2));
                AnsiConsole.MarkupLine("[green]published live pack (check commission on install)[/]");
            }
            else if (t[0] == "install")
                await grains.GetGrain<IMarketplaceNeuron>("market-main").FireAsync(new InstallFromMarketplace("LiveTestPack", "1.0", "live-buyer"));
            else if (t[0] == "timeline" && t.Length > 1)
            {
                var tl = await grains.GetGrain<INeuron>(t[1]).GetTimelineAsync();
                AnsiConsole.WriteLine(string.Join("\n", tl.TakeLast(3).Select(x => x.Type)));
            }
            else if (t[0] == "list")
            {
                var m = grains.GetGrain<IMarketplaceNeuron>("market-main");
                await m.FireAsync(new ListPublished());
                var tl = await m.GetTimelineAsync();
                if (tl.LastOrDefault(s => s is PublishedList) is PublishedList pl) AnsiConsole.WriteLine(pl.Packs.Count + " packs");
            }
        }
        catch (Exception e) { AnsiConsole.MarkupLine("[red]" + e.Message + "[/]"); }
    }
}

AnsiConsole.MarkupLine("[grey]GrokCLI TUI exited.[/]");
return 0;

