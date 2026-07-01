using DigitalBrain.Core;
using Orleans;
using System.Text.Json;

namespace DigitalBrain.Mcp.Tools;

// Shared, transport-agnostic helpers for the DigitalBrain MCP tool surfaces. Reached through an in-process
// IGrainFactory when co-hosted in the silo (HTTP) and the Orleans-client IGrainFactory in the stdio server.
// No fabricated fallbacks: real responses or honest errors only.
public abstract class DigitalBrainToolsBase(IGrainFactory grains)
{
    protected IGrainFactory Grains { get; } = grains;

    protected static readonly JsonSerializerOptions SurfaceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    protected static IEnumerable<string> SplitIds(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id));

    protected async Task<IReadOnlyList<NeuroPack>> GetPublishedPacksWithLocalSeedsAsync(IMarketplaceNeuron marketplace)
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

    protected static async Task<IReadOnlyList<NeuroPack>> ReadLatestPublishedPacksAsync(IMarketplaceNeuron marketplace)
    {
        var timeline = await marketplace.GetTimelineAsync();
        return timeline.OfType<PublishedList>().LastOrDefault()?.Packs ?? Array.Empty<NeuroPack>();
    }

    protected static string PackKey(NeuroPack pack) => $"{pack.Name}@{pack.Version}";

    protected static string Explain(Exception exception)
    {
        var root = exception.GetBaseException();
        return root.Message == exception.Message
            ? exception.Message
            : $"{exception.Message} ({root.Message})";
    }

    protected INeuron ResolveNeuron(string neuronId)
    {
        if (neuronId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
        {
            return Grains.GetGrain<INeuron>(neuronId);
        }

        return neuronId switch
        {
            "aspire-main" => Grains.GetGrain<IAspireNeuron>(neuronId),
            "closedloop-main" => Grains.GetGrain<IClosedLoopNeuron>(neuronId),
            "compiler-main" => Grains.GetGrain<ICompiler>(neuronId),
            "context-main" => Grains.GetGrain<INeuron>(neuronId),
            "company-main" => Grains.GetGrain<ICompanyKnowledgeNeuron>(neuronId),
            "company-skill-main" => Grains.GetGrain<ICompanySkillOrchestratorNeuron>(neuronId),
            "chart-main" => Grains.GetGrain<IDataVisualizationNeuron>(neuronId),
            _ when neuronId.StartsWith("chart-", StringComparison.OrdinalIgnoreCase) => Grains.GetGrain<IChartNeuron>(neuronId),
            "db-main" => Grains.GetGrain<IDbSupportNeuron>(neuronId),
            "foundry-main" => Grains.GetGrain<ICodeFoundryLoopNeuron>(neuronId),
            "ino-editor-main" => Grains.GetGrain<IInoCodeEditor>(neuronId),
            "ino-main" => Grains.GetGrain<IInoNeuron>(neuronId),
            "llm-main" => Grains.GetGrain<ILlmNeuron>(neuronId),
            "market-main" => Grains.GetGrain<IMarketplaceNeuron>(neuronId),
            "status-main" => Grains.GetGrain<ISystemStatus>(neuronId),
            _ => Grains.GetGrain<IDemoNeuron>(neuronId)
        };
    }

    protected static JsonElement ReadObject(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Object ? value.Value : default;
    }

    protected static string? ReadString(JsonElement element, string propertyName)
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

    protected static JsonElement? ReadElement(JsonElement element, string propertyName)
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
