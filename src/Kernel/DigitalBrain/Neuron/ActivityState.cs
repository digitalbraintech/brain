using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

/// <summary>A durable reduction of explicit work facts, never a timeout-based guess.</summary>
[GenerateSerializer]
internal sealed class ActivityState
{
    [Id(0)] public ActivityExecutionChanged? Root { get; set; }
    [Id(1)] public Dictionary<string, ActivityExecutionChanged> Operations { get; set; } = [];
    [Id(2)] public List<ActivityExecutionChanged> Events { get; set; } = [];
    [Id(3)] public bool RootSettled { get; set; }
    [Id(4)] public long Version { get; set; }

    public bool Apply(ActivityExecutionChanged fact)
    {
        if (Operations.TryGetValue(fact.OperationId, out var previous)
            && (previous.Timestamp > fact.Timestamp
                || previous == fact
                || (previous.Timestamp == fact.Timestamp && IsTerminal(previous.Phase) && !IsTerminal(fact.Phase))))
        {
            return false;
        }

        Root ??= fact;
        if (fact.CausationId is null && Root.CausationId is not null)
        {
            Root = fact;
        }
        if (fact.CausationId is null && IsTerminal(fact.Phase))
        {
            RootSettled = true;
        }
        Operations[fact.OperationId] = fact;
        Version++;
        Events.Add(fact);
        // The current operation ledger remains intact when the display trace rolls over.
        if (Events.Count > 512)
        {
            Events.RemoveRange(0, Events.Count - 512);
        }
        return true;
    }

    public ActivityView View()
    {
        var root = Root ?? throw new InvalidOperationException("An activity requires an execution fact.");
        var values = Operations.Values;
        var status = values.Any(f => f.Phase == "running") ? "running"
            : values.Any(f => f.Phase == "waiting") ? "waiting"
            : values.Any(f => f.Phase == "failed") ? "failed"
            : values.Any(f => f.Phase == "cancelled") ? "cancelled"
            : RootSettled ? "completed" : "observed";
        var latest = values.MaxBy(f => f.Timestamp)!;
        var id = root.CorrelationId.ToString();
        var participants = values.SelectMany<ActivityExecutionChanged, string>(f => f.Target is { } target
                ? [Instance(f.Source), Instance(target)] : [Instance(f.Source)])
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(id, id, root.SignalId.ToString(), root.SignalType, root.Title ?? root.SignalType,
            status, root.Timestamp, latest.Timestamp, participants,
            Events.Select(f => new ActivityEventView(f.OperationId, f.SignalId.ToString(), f.CausationId?.ToString(),
                Instance(f.Source), f.Target is { } target ? Instance(target) : null, f.SignalType, f.Phase,
                f.Timestamp, f.BehaviorRevision, f.Detail)).ToArray(), root.CommandId,
            latest.Detail ?? (status == "observed" ? "Observed signal; no tracked execution has settled it." : null), root.Principal, Version);
    }

    private static bool IsTerminal(string phase) => phase is "completed" or "failed" or "cancelled";
    private static string Instance(Abstractions.Identity.NeuronId id) => $"{id.Type}:{id.Name}";
}
