// GrokCLI - functional Orleans client proxy to NeuroOS neurons (MCP/CLI simulation)
using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

var host = Host.CreateApplicationBuilder(args);

host.AddKeyedRedisClient("redis");
host.UseOrleansClient();

using var app = host.Build();

IGrainFactory? grains = null;
try
{
    await app.StartAsync();
    grains = app.Services.GetRequiredService<IGrainFactory>();
    Console.WriteLine("NeuroOS GrokCLI v2 (prototype) - connected to brain");
}
catch
{
    Console.WriteLine("NeuroOS GrokCLI v2 (prototype) - standalone demo mode (no brain cluster)");
}

if (args.Length > 0 && args[0] == "create-neuron")
{
    var desc = args.Length > 1 ? string.Join(" ", args[1..]) : "default neuron";
    Console.WriteLine($"[CLI] Firing CreateNeuronRequest to CompilerNeuron: {desc}");

    if (grains != null)
    {
        try
        {
            var compiler = grains.GetGrain<ICompiler>("compiler-main");
            await compiler.FireAsync(new CreateNeuronRequest(desc));

            var timeline = await compiler.GetTimelineAsync();
            var generated = timeline.LastOrDefault(s => s.Type == nameof(NeuronCodeGenerated)) as NeuronCodeGenerated;
            if (generated != null)
            {
                Console.WriteLine($"[CLI] Generated: {generated.GeneratedCodeSnippet}");
            }
            Console.WriteLine("[CLI] -> Codegen complete. (Reqnroll/LLM sim + Aspire reload in full impl)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLI] Connection to brain failed (start the AppHost first): {ex.Message}");
            Console.WriteLine($"[CLI] Simulating: Generated stub for '{desc}'");
        }
    }
    else
    {
        Console.WriteLine($"[CLI] Simulating: Generated stub for '{desc}'");
    }
}
else if (args.Length > 0 && args[0] == "optimize")
{
    Console.WriteLine("[CLI] Would fire telemetry to trigger MetaOptimizer proposals");
}
else
{
    Console.WriteLine("Usage: grok create-neuron \"description\" | grok optimize");
    Console.WriteLine("Example: grok create-neuron \"I want Gmail + top senders + Excel chart\"");
}

if (grains != null)
{
    await app.StopAsync();
}
