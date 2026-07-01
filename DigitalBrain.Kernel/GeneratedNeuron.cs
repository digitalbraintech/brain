using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.AI;
using Microsoft.CodeAnalysis;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.generated")]
public class GeneratedNeuron : Neuron, IGeneratedNeuron, IHandle<NeuronTelemetry>
{
    private EmbodiedPack? _embodied;

    public GeneratedNeuron(ILogger<GeneratedNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public Task HandleAsync(NeuronTelemetry telemetry) => Task.CompletedTask;

    protected override bool ShouldSubscribeToTimeline => true;

    public override async Task OnNextAsync(Synapse item, Orleans.Streams.StreamSequenceToken? token = null)
    {
        EnsureEmbodied();
        if (await TryDispatchEmbodiedAsync(item))
            return;

        await base.OnNextAsync(item, token);
    }

    public override Task OnDeactivateAsync(Orleans.DeactivationReason reason, CancellationToken cancellationToken)
    {
        var toUnload = _embodied;
        _embodied = null;
        toUnload?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        var id = this.GetPrimaryKeyString() ?? "unknown-generated";
        Logger.LogInformation("GeneratedNeuron {Id} dispatched {Type}", id, synapse.Type);
        await FireAsync(new NeuronTelemetry(Self, "generated-dispatched"));

        switch (synapse)
        {
            case NeuroPackInstalled installed:
                TryEmbody(installed.Pack);
                await EmitConfigFormIfRequiredAsync();
                return;
        }

        if (await TryDispatchEmbodiedAsync(synapse))
        {
            return;
        }

        switch (synapse)
        {
            case DemoMessageSynapse msg:
                Logger.LogInformation("Generated handled message: {Text}", msg.Text);
                break;
            case ExperienceUsed used:
                await UseExperienceAsync(used);
                break;
        }
    }

    private void TryEmbody(NeuroPack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.Code))
            return;

        var embodier = ServiceProvider.GetService<IPackEmbodiment>();
        if (embodier is null)
        {
            Logger.LogWarning("No IPackEmbodiment registered; pack '{Pack}' will use the LLM fallback.", pack.Name);
            return;
        }

        try
        {
            _embodied?.Dispose();
            _embodied = embodier.Embody(pack.Name, pack.Code);
            Logger.LogInformation("GeneratedNeuron EMBODIED pack {Name}@{Ver} as real compiled C#.", pack.Name, pack.Version);
        }
        catch (PackEmbodimentException ex)
        {
            _embodied = null;
            Logger.LogWarning(ex, "Pack '{Pack}' is not a compilable IPackBehavior; using LLM fallback on use.", pack.Name);
        }
    }

    private async Task EmitConfigFormIfRequiredAsync()
    {
        if (_embodied is null) return;

        var required = _embodied.GetManifest().RequiredConfig;
        if (required is null || required.Count == 0) return;

        var surface = ConfigFormSurface.Build(_embodied.PackName, required, Self.Value);
        await FireAsync(surface);
        ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
        Logger.LogInformation("GeneratedNeuron emitted config form for pack '{Pack}' ({FieldCount} fields).", _embodied.PackName, required.Count);
    }

    private void EnsureEmbodied()
    {
        if (_embodied is not null) return;
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last is not null)
        {
            TryEmbody(last.Pack);
            return;
        }
        const string generatedPrefix = "generated-";
        var packName = this.GetPrimaryKeyString() ?? string.Empty;
        if (packName.StartsWith(generatedPrefix, StringComparison.OrdinalIgnoreCase))
            packName = packName[generatedPrefix.Length..];
        var seed = MarketplaceSeeds.LocalUiPacks.FirstOrDefault(pack =>
            string.Equals(pack.Name, packName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pack.Code));
        if (seed is not null)
            TryEmbody(seed);
    }

    private async Task<bool> TryDispatchEmbodiedAsync(Synapse synapse)
    {
        EnsureEmbodied();
        if (_embodied is null || !_embodied.CanHandle(synapse))
        {
            return false;
        }

        var manifest = _embodied.GetManifest();
        IReadOnlyList<Synapse> outputs;
        try
        {
            outputs = _embodied.Handle(synapse);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Embodied pack '{Pack}' failed while handling {SynapseType}.", _embodied.PackName, synapse.Type);
            await FireAsync(new PackEmission(_embodied.PackName, synapse.Type, "pack-error:" + ex.GetBaseException().Message));
            return true;
        }

        foreach (var output in outputs)
        {
            var normalized = NormalizePackOutput(_embodied.PackName, output);
            await Broadcast(normalized);
            BroadcastPackSurface(normalized, _embodied.PackName);
        }

        Logger.LogInformation(
            "GeneratedNeuron dispatched {SynapseType} to embodied pack '{Pack}' (manifest: {ManifestTypes}) and emitted {Count} synapse(s).",
            synapse.Type,
            _embodied.PackName,
            string.Join(',', manifest.HandledSynapseTypes.Select(t => t.Value)),
            outputs.Count);
        return true;
    }

    private static Synapse NormalizePackOutput(string packName, Synapse output)
    {
        var normalized = output is PackEmission emission
            ? emission with { Pack = packName }
            : output;

        return normalized with
        {
            CorrelationId = null,
            CausationId = null,
            SynapseId = Guid.NewGuid().ToString("N")
        };
    }

    private void BroadcastPackSurface(Synapse output, string packName)
    {
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is null) return;

        if (output is UiSurface surface)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, packName));
        }
        else if (output is RfwCard card)
        {
            bus.Broadcast(card);
        }
    }

    private async Task UseExperienceAsync(ExperienceUsed used)
    {
        if (IsGmailInsightsExperience(used))
        {
            await RunGmailInsightsExperienceAsync(used);
            return;
        }

        EnsureEmbodied();

        if (_embodied is not null)
        {
            var output = _embodied.Respond(used.Action);
            await FireAsync(new PackEmission(_embodied.PackName, used.Action, output));
            Logger.LogInformation("GeneratedNeuron ran embodied pack '{Pack}' for action '{Action}'", _embodied.PackName, used.Action);
            if (used.Action is "open" or "emit-test-surface" or "self-test")
            {
                var winTree = new UiWidgetTree("fcard", new Dictionary<string, object?> { ["title"] = used.Pack + " - " + used.Action }, new List<UiWidgetTree> { new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "Live from embodied " + used.Pack }) });
                var surf = new UiSurface(used.Pack, new Dictionary<string, object?> { [UiSurfaceKeys.Title] = used.Pack, ["pack"] = used.Pack, ["tree"] = winTree });
                await FireAsync(surf);
                var b = ServiceProvider.GetService<HomeFeedBus>();
                b?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surf, Self.Value));
            }
        }

        var inst = LastInstalledPack();
        if (inst is null)
        {
            Logger.LogInformation("Generated experience {Pack} used: {Action} (no installed pack yet).", used.Pack, used.Action);
            return;
        }

        var (packKey, code, desc) = inst.Value;
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            await FireAsync(new LlmResponse(used.Pack, $"[Embodied: {packKey}] Simulated response to {used.Action} using installed experience.", "sim"));
        }
        else
        {
            var behaviorPrompt = $"You are now the installed experience '{packKey}'.\n" +
                                 $"Description: {desc}\n" +
                                 $"Implementation guidance/code:\n{code}\n\n" +
                                 $"Handle the following usage: {used.Action} on input related to '{used.Pack}'.\n" +
                                 "Respond in character as this specific installed neuron/experience would. Be concise and useful.";
            var response = await chat.GetResponseAsync(behaviorPrompt);
            await FireAsync(new LlmResponse(behaviorPrompt, response.Text.Trim(), "embodied-pack"));
            Logger.LogInformation("GeneratedNeuron LLM-embodied pack '{Pack}' for action '{Action}'", packKey, used.Action);
        }

        if (used.Action is "open" or "emit-test-surface" or "self-test")
        {
            var winTree = new UiWidgetTree("fcard", new Dictionary<string, object?> { ["title"] = used.Pack + " - " + used.Action }, new List<UiWidgetTree> { new UiWidgetTree("text", new Dictionary<string, object?> { ["text"] = "Live surface from " + used.Pack + " pack scenario." }) });
            var surf = new UiSurface(used.Pack, new Dictionary<string, object?>
            {
                [UiSurfaceKeys.Title] = used.Pack,
                ["pack"] = used.Pack,
                ["tree"] = winTree
            });
            await FireAsync(surf);
            var bus = ServiceProvider.GetService<HomeFeedBus>();
            if (bus != null)
            {
                bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surf, Self.Value));
            }
        }
    }

    private async Task RunGmailInsightsExperienceAsync(ExperienceUsed used)
    {
        var userId = EffectiveUserId(used.UserId);
        var emails = BuildGmailSampleRows(100);
        var categoryRows = emails
            .GroupBy(row => row["category"]?.ToString() ?? "Other", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => new Dictionary<string, object?>
            {
                ["category"] = group.Key,
                ["count"] = group.Count()
            })
            .ToArray();

        var summary = await SummarizeGmailRowsAsync(emails);
        var chartRequestId = "gmail-last-100-" + StableKey(userId);
        await FireAsync(new PackEmission(used.Pack, used.Action, summary));

        var surface = BuildGmailInsightsSurface(used, summary, emails.Count, chartRequestId);
        await FireAsync(surface);
        ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));

        var chart = GrainFactory.GetGrain<IDataVisualizationNeuron>("chart-" + chartRequestId);
        await chart.FireAsync(new VisualizeDataRequest(
            "Gmail last 100 emails by category",
            System.Text.Json.JsonSerializer.Serialize(categoryRows),
            "bar",
            chartRequestId,
            userId,
            used.SessionId));
    }

    private async Task<string> SummarizeGmailRowsAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> emails)
    {
        var fallback = $"Local Gmail Insights analyzed {emails.Count} messages. Top categories: " +
            string.Join(", ", emails
                .GroupBy(row => row["category"]?.ToString() ?? "Other", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Take(3)
                .Select(group => group.Key + " " + group.Count()));

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            return fallback;
        }

        var sample = string.Join("\n", emails.Take(20).Select(row =>
            "- " + row["from"] + " | " + row["subject"] + " | " + row["category"]));
        try
        {
            var response = await chat.GetResponseAsync(
                "You are the local DigitalBrain Gmail insights experience. " +
                "Summarize these recent Gmail messages in two concise bullets and name the dominant categories.\n" +
                sample);
            var text = response.Text.Trim();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Local LLM Gmail summary failed; using deterministic fallback.");
            return fallback;
        }
    }

    private UiSurface BuildGmailInsightsSurface(ExperienceUsed used, string summary, int emailCount, string chartRequestId)
    {
        var tree = new UiWidgetTree(
            "fcard",
            new Dictionary<string, object?>
            {
                ["title"] = "Gmail Insights",
                ["subtitle"] = emailCount + " messages analyzed locally"
            },
            new List<UiWidgetTree>
            {
                new("text", new Dictionary<string, object?> { ["text"] = summary }),
                new("text", new Dictionary<string, object?> { ["text"] = "Chart request: " + chartRequestId })
            });

        return new UiSurface("gmail-insights", new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "surface.gmail-insights." + chartRequestId,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Gmail Insights",
            [UiSurfaceKeys.Priority] = 30,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["pack"] = used.Pack,
            ["action"] = used.Action,
            ["userId"] = EffectiveUserId(used.UserId),
            ["sessionId"] = used.SessionId,
            ["emailCount"] = emailCount,
            ["summary"] = summary,
            ["chartRequestId"] = chartRequestId,
            ["source"] = "local-sample",
            ["tree"] = tree
        });
    }

    private static bool IsGmailInsightsExperience(ExperienceUsed used) =>
        used.Pack.Equals("DigitalBrain.Experience.GmailInsights", StringComparison.OrdinalIgnoreCase) ||
        used.Action.StartsWith("gmail:", StringComparison.OrdinalIgnoreCase);

    private static string EffectiveUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();

    private static string StableKey(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        var key = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "anonymous" : key;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> BuildGmailSampleRows(int count)
    {
        string[] senders =
        [
            "alerts@github.com",
            "billing@cloud.local",
            "calendar@google.com",
            "team@digitalbrain.local",
            "newsletter@aiweekly.example",
            "support@customer.example",
            "security@accounts.google.com",
            "noreply@stripe.com"
        ];
        string[] categories = ["Engineering", "Billing", "Calendar", "Team", "Newsletter", "Support", "Security", "Payments"];
        string[] subjects =
        [
            "Build completed for kernel runtime",
            "Invoice available for review",
            "Meeting moved to tomorrow",
            "Product surface review notes",
            "Local AI tooling digest",
            "Customer follow-up requested",
            "Security alert for account access",
            "Payment receipt"
        ];

        var now = DateTimeOffset.UtcNow;
        var rows = new List<IReadOnlyDictionary<string, object?>>(count);
        for (var i = 0; i < count; i++)
        {
            var ix = i % categories.Length;
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = "gmail-local-" + (i + 1).ToString("000"),
                ["receivedAt"] = now.AddMinutes(-37 * i).ToString("O"),
                ["from"] = senders[ix],
                ["subject"] = subjects[ix] + " #" + (i + 1),
                ["category"] = categories[ix],
                ["importance"] = ix is 0 or 5 or 6 ? "high" : "normal"
            });
        }

        return rows;
    }

    private (string Key, string Code, string Description)? LastInstalledPack()
    {
        var last = OutgoingJournal.Concat(IncomingJournal).OfType<NeuroPackInstalled>().LastOrDefault();
        if (last is null) return null;
        var p = last.Pack;
        return ($"{p.Name}@{p.Version}", p.Code, p.Description);
    }

}


