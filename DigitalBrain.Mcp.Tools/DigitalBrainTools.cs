using DigitalBrain.Core;
using ModelContextProtocol.Server;
using Orleans;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

// MCP tool surface over the DigitalBrain neuron cluster. Reached through an in-process IGrainFactory when
// co-hosted inside the silo (HTTP transport) and through the Orleans-client IGrainFactory when run as the
// standalone stdio server — the tools are transport- and topology-agnostic. Split into partial files by concern;
// shared helpers live here. No fabricated/[SIMULATED]/[DEMO] fallbacks: real responses or honest errors only.
[McpServerToolType]
public partial class DigitalBrainTools(IGrainFactory grains)
{
    private static readonly JsonSerializerOptions SurfaceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static IEnumerable<string> SplitIds(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id));

    private static async Task<IReadOnlyList<NeuroPack>> GetPublishedPacksWithLocalSeedsAsync(IMarketplaceNeuron marketplace)
    {
        await marketplace.FireAsync(new ListPublished());
        var published = await ReadLatestPublishedPacksAsync(marketplace);
        var publishedKeys = published.Select(PackKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingLocalPacks = MarketplaceSeeds.LocalUiPacks
            .Where(pack => !publishedKeys.Contains(PackKey(pack)))
            .ToArray();

        if (missingLocalPacks.Length == 0)
        {
            return published;
        }

        foreach (var pack in missingLocalPacks)
        {
            await marketplace.FireAsync(MarketplaceSeeds.ToPublishCommand(pack));
        }

        await marketplace.FireAsync(new ListPublished());
        return await ReadLatestPublishedPacksAsync(marketplace);
    }

    private static async Task<IReadOnlyList<NeuroPack>> ReadLatestPublishedPacksAsync(IMarketplaceNeuron marketplace)
    {
        var timeline = await marketplace.GetTimelineAsync();
        return timeline.OfType<PublishedList>().LastOrDefault()?.Packs ?? Array.Empty<NeuroPack>();
    }

    private static string PackKey(NeuroPack pack) => $"{pack.Name}@{pack.Version}";

    private static string Explain(Exception exception)
    {
        var root = exception.GetBaseException();
        return root.Message == exception.Message
            ? exception.Message
            : $"{exception.Message} ({root.Message})";
    }

    private INeuron ResolveNeuron(string neuronId)
    {
        if (neuronId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
        {
            return grains.GetGrain<IKernelTask>(neuronId);
        }

        return neuronId switch
        {
            "aspire-main" => grains.GetGrain<IAspireNeuron>(neuronId),
            "closedloop-main" => grains.GetGrain<IClosedLoopNeuron>(neuronId),
            "compiler-main" => grains.GetGrain<ICompiler>(neuronId),
            "context-main" => grains.GetGrain<IContextNeuron>(neuronId),
            "company-main" => grains.GetGrain<ICompanyKnowledgeNeuron>(neuronId),
            "company-skill-main" => grains.GetGrain<ICompanySkillOrchestratorNeuron>(neuronId),
            "chart-main" => grains.GetGrain<IDataVisualizationNeuron>(neuronId),
            "db-main" => grains.GetGrain<IDbSupportNeuron>(neuronId),
            "foundry-main" => grains.GetGrain<ICodeFoundryLoopNeuron>(neuronId),
            "ino-editor-main" => grains.GetGrain<IInoCodeEditor>(neuronId),
            "ino-main" => grains.GetGrain<IInoNeuron>(neuronId),
            "llm-main" => grains.GetGrain<ILlmNeuron>(neuronId),
            "market-main" => grains.GetGrain<IMarketplaceNeuron>(neuronId),
            "status-main" => grains.GetGrain<ISystemStatus>(neuronId),
            _ => grains.GetGrain<IDemoNeuron>(neuronId)
        };
    }

    private static JsonElement ReadObject(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Object ? value.Value : default;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static JsonElement? ReadElement(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}

