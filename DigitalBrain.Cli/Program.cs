// GrokCLI TUI - interactive Text User Interface for NeuroOS (MCP/CLI proxy to neurons)
using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Spectre.Console;

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
    AnsiConsole.MarkupLine("[green]NeuroOS GrokCLI v2 TUI - connected to brain[/]");
}
catch
{
    AnsiConsole.MarkupLine("[yellow]NeuroOS GrokCLI v2 TUI - standalone demo mode (no brain cluster)[/]");
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
                    if (generated != null)
                    {
                        AnsiConsole.MarkupLine("[green]Generated code:[/]");
                        AnsiConsole.WriteLine(generated.GeneratedCodeSnippet);
                    }

                    lastCreatedPack = "Generated-" + desc.Replace(" ", "").Replace("\"", "").Substring(0, Math.Min(20, desc.Length));
                    AnsiConsole.MarkupLine($"[green]New experience created as pack: {lastCreatedPack}. Now use 'Publish Experience to Marketplace' to share it.[/]");
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
                    await marketplace.FireAsync(new PublishToMarketplace(pubPack, pubVer));
                    AnsiConsole.MarkupLine($"[green]Published {pubPack}@{pubVer} to Marketplace.[/]");
                    AnsiConsole.MarkupLine("[green]Others can now List Published Packs, Download/Install, and Use it via GeneratedNeuron.[/]");
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
                        AnsiConsole.MarkupLine("[green]Published packs (newly created ones ready for download):[/]");
                        foreach (var p in list.Packs) AnsiConsole.WriteLine($"  - {p}");
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
                    await marketplace.FireAsync(new InstallFromMarketplace(instPack, instVer));
                    AnsiConsole.MarkupLine($"[green]Downloaded and installed {instPack}@{instVer}.[/]");
                    AnsiConsole.MarkupLine("[green]Experience activated (GeneratedNeuron ready for use).[/]");
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
                        AnsiConsole.MarkupLine("[green]Installed/available packs:[/]");
                        foreach (var p in list.Packs) AnsiConsole.WriteLine($"  - {p}");
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

AnsiConsole.MarkupLine("[grey]GrokCLI TUI exited.[/]");
