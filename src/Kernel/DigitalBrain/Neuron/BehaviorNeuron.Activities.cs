using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed partial class BehaviorNeuron
{
    private async Task ReportBehaviorTransitions(BehaviorState previous, BehaviorState current)
    {
        var before = ActivityWorkOf(previous);
        var after = ActivityWorkOf(current);
        foreach (var (id, work) in after)
        {
            if (!before.TryGetValue(id, out var old) || old.Phase != work.Phase || old.Detail != work.Detail)
            {
                await ReportActivityAsync(work.Input, $"behavior:{Id}:{id:N}", work.Phase,
                    work.Detail, work.Revision.ToString("N")).ConfigureAwait(true);
            }
        }

        foreach (var (id, work) in before)
        {
            if (after.ContainsKey(id))
            {
                continue;
            }
            var cancelled = !current.Enabled || current.Epoch != previous.Epoch || !IsDeliveryCurrent(work.Input)
                || work.Subject is { } subject && current.Subjects.TryGetValue(subject, out var latest)
                    && latest.Generation != work.Generation;
            await ReportActivityAsync(work.Input, $"behavior:{Id}:{id:N}", cancelled ? "cancelled" : "completed",
                cancelled ? "Behavior work was fenced or disabled." : "Behavior work and its output delivery settled.",
                work.Revision.ToString("N")).ConfigureAwait(true);
        }
    }

    private static Dictionary<Guid, ActivityWork> ActivityWorkOf(BehaviorState state)
    {
        var result = state.Work.ToDictionary(work => work.Id, work => new ActivityWork(work.Input,
            work.Program.Revision, work.Terminal ? "failed" : work.ClaimToken is null ? "waiting" : "running",
            work.Detail, work.SubjectKey, work.SubjectGeneration));
        foreach (var output in state.Outbox)
        {
            if (output.WorkId != Guid.Empty && output.Input is { } input)
            {
                result[output.WorkId] = new(input, output.ProgramRevision, "waiting", "Output delivery pending.",
                    output.SubjectKey, output.SubjectGeneration);
            }
        }
        return result;
    }

    private sealed record ActivityWork(SignalDelivery Input, Guid Revision, string Phase, string? Detail,
        string? Subject, long? Generation);
}
