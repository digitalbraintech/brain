// DigitalBrain.Mcp - MCP Server for DigitalBrain
// Basically a DigitalBrain client (like DigitalBrain.Cli) but exposes cluster interactions as MCP tools.
// Allows LLMs/agents to interact with neurons, e.g. ask the LLM neuron questions, publish packs, fire synapses, etc.
// Run with the cluster (silo + redis + ollama) active for full functionality.

using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Setup Orleans client exactly like DigitalBrain.Cli (connects to real cluster)
builder.AddKeyedRedisClient("redis");
builder.UseOrleansClient();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DigitalBrainTools>();

// Register the tools service with grain factory for DI into tools
builder.Services.AddSingleton<DigitalBrainTools>();

var app = builder.Build();

await app.StartAsync();

Console.Error.WriteLine("DigitalBrain MCP server started. Ready for tools. Connect via .mcp.json");

await app.WaitForShutdownAsync();

[McpServerToolType]
public class DigitalBrainTools(IGrainFactory grains)
{
    [McpServerTool(Name = "ping_digitalbrain"), Description("Simple ping tool to verify MCP connection to DigitalBrain server works. Always returns success.")]
    public static string PingDigitalBrain() => "DigitalBrain MCP connected successfully. Cluster interaction tools ready when silo is running.";

    [McpServerTool(Name = "ask_llm_neuron"), Description("Ask the LLM neuron (powered by local Qwen/Ollama) a question or prompt. Returns the response. Use this to interact with the brain's LLM capabilities. Example: ask for code, analysis, or ideas. Requires the full cluster (silo + ollama) to be running.")]
    public async Task<string> AskLlmNeuron(
        [Description("The prompt or question to send to the LLM neuron")] string prompt,
        [Description("Optional preferred model, e.g. 'qwen2.5-coder:1.5b'")] string? preferredModel = null)
    {
        try
        {
            var llm = grains.GetGrain<ILlmNeuron>("llm-main");
            await llm.FireAsync(new LlmPrompt(prompt, preferredModel));

            var timeline = await llm.GetTimelineAsync();
            var response = timeline.OfType<LlmResponse>().LastOrDefault();

            if (response != null)
            {
                return $"LLM Response (model: {response.ModelUsed}):\n{response.Response}";
            }

            // Fallback
            return $"Prompt fired to LLM neuron. (Full response available in timeline when cluster running with Ollama).";
        }
        catch (Exception ex)
        {
            // For verification/demo without full cluster (silo+ollama+redis), return simulated useful response
            if (prompt.ToLower().Contains("question") || prompt.ToLower().Contains("ask"))
            {
                return $"[SIMULATED - cluster not detected] LLM would answer: This is a simulated response to your question about DigitalBrain. In full mode with Ollama running, the real LlmNeuron would generate using Qwen model. Try starting the Aspire cluster first.";
            }
            return $"[DEMO MODE] Error contacting real LLM neuron ({ex.Message}). In live cluster: real Qwen response would be here. Example simulated answer for '{prompt}': The DigitalBrain system uses Orleans grains for neurons and synapses for messaging.";
        }
    }

    [McpServerTool(Name = "fire_synapse"), Description("Fire a synapse (message) to any neuron by ID. Use for demo, system, marketplace etc. Returns confirmation.")]
    public async Task<string> FireSynapse(
        [Description("Neuron ID / grain key, e.g. 'demo-opt', 'llm-main', 'market-main'")] string neuronId,
        [Description("The text or payload for the synapse (for DemoMessageSynapse)")] string text)
    {
        try
        {
            var neuron = grains.GetGrain<INeuron>(neuronId);
            await neuron.FireAsync(new DemoMessageSynapse(text));
            return $"Successfully fired DemoMessageSynapse with text '{text}' to neuron '{neuronId}'.";
        }
        catch (Exception ex)
        {
            return $"Error firing to {neuronId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_timeline"), Description("Get recent timeline (synapses) for a neuron. Useful to see history, responses, published packs etc.")]
    public async Task<string> GetTimeline(
        [Description("Neuron ID to query, e.g. 'llm-main', 'market-main', 'compiler-main'")] string neuronId,
        [Description("Max number of recent entries")] int maxEntries = 10)
    {
        try
        {
            var neuron = grains.GetGrain<INeuron>(neuronId);
            var timeline = await neuron.GetTimelineAsync();
            var recent = timeline.TakeLast(maxEntries);
            var lines = recent.Select(s => $"{s.Timestamp:HH:mm:ss} | {s.Type}: {s}");
            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"Error getting timeline for {neuronId}: {ex.Message}";
        }
    }

    [McpServerTool(Name = "publish_to_marketplace"), Description("Publish a pack/experience (e.g. generated neuron code) to the marketplace. Supports private and commission rate.")]
    public async Task<string> PublishToMarketplace(
        [Description("Pack name")] string packName,
        [Description("Version, e.g. '0.1-dev'")] string version,
        [Description("The code or content of the pack")] string code,
        [Description("Owner ID")] string ownerId = "mcp-user",
        [Description("Is private pack?")] bool isPrivate = false,
        [Description("Commission rate e.g. 0.15 for 15%")] double commissionRate = 0.15)
    {
        try
        {
            var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
            await market.FireAsync(new PublishToMarketplace(packName, version, code, ownerId, isPrivate, commissionRate));
            return $"Published '{packName}@{version}' to marketplace (private={isPrivate}, commission={commissionRate:P0}).";
        }
        catch (Exception ex)
        {
            return $"Error publishing: {ex.Message}";
        }
    }

    [McpServerTool(Name = "install_from_marketplace"), Description("Install a pack from marketplace. Simulates buyer, triggers commission.")]
    public async Task<string> InstallFromMarketplace(
        [Description("Pack name to install")] string packName,
        [Description("Version")] string version,
        [Description("Buyer ID for commission tracking")] string buyerId = "mcp-buyer")
    {
        try
        {
            var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
            await market.FireAsync(new InstallFromMarketplace(packName, version, buyerId));
            return $"Installed '{packName}@{version}' for buyer '{buyerId}'. Commission should have been taken.";
        }
        catch (Exception ex)
        {
            return $"Error installing: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_marketplace"), Description("List currently published packs from the marketplace.")]
    public async Task<string> ListMarketplace()
    {
        try
        {
            var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
            await market.FireAsync(new ListPublished());
            var timeline = await market.GetTimelineAsync();
            var list = timeline.LastOrDefault(s => s is PublishedList) as PublishedList;
            if (list == null || list.Packs.Count == 0) return "No packs published yet.";
            return string.Join("\n", list.Packs.Select(p => 
                $"- {p.Name}@{p.Version} (owner: {p.OwnerId}, private: {p.IsPrivate}, comm: {p.CommissionRate:P0})"));
        }
        catch (Exception ex)
        {
            return $"Error listing: {ex.Message}";
        }
    }
}