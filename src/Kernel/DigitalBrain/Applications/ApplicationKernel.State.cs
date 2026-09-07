using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Core;

internal sealed partial class ApplicationKernel
{
    private Task SaveCheckpoint(ApplicationWork work, Dictionary<string, string> writes)
    {
        if (IsQuery(work))
        {
            if (writes.Count != 0) { throw new InvalidOperationException("Queries cannot write state."); }
            return Save(work);
        }
        if (work.StateScope is null)
        {
            if (writes.Count != 0) { throw new InvalidOperationException("An operation without shared state cannot write it."); }
            return Save(work);
        }
        var scope = storage.State.Scopes[work.StateScope];
        if (scope.Version != work.ExpectedStateVersion)
        {
            throw new InvalidOperationException("The shared state version changed while this input was executing.");
        }
        if (writes.Count == 0) { return Save(work); }
        var schema = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(scope.Schema)!;
        if (writes.Keys.Any(key => !schema.ContainsKey(key)))
        {
            throw new InvalidOperationException("A checkpoint contains undeclared state.");
        }
        var values = new Dictionary<string, string>(scope.Values, StringComparer.Ordinal);
        foreach (var (key, value) in writes) { values[key] = value; }
        var next = scope with { Version = checked(scope.Version + 1), Values = values };
        return Commit(storage.State with
        {
            Scopes = new(storage.State.Scopes) { [work.StateScope] = next },
            Work = new(storage.State.Work) { [work.Id] = work with { ExpectedStateVersion = next.Version } },
        });
    }

    private bool HasEarlierStateInput(ApplicationWork work)
        => !IsQuery(work) && work.StateScope is not null && storage.State.Work.Values.Any(other =>
            other.StateScope == work.StateScope && other.AdmissionIndex < work.AdmissionIndex
            && (other.Result.Status is "pending" or "waiting") && !IsQuery(other));

    private bool IsQuery(ApplicationWork work)
        => storage.State.Revisions[work.Revision].Operations.Single(operation => operation.Key == work.Operation).IsQuery;

    private ApplicationWork WithInitialState(ApplicationWork work)
    {
        if (work.StateScope is null || work.InitialState is not null) { return work; }
        var scope = storage.State.Scopes[work.StateScope];
        return work with { InitialState = new(scope.Values), ExpectedStateVersion = scope.Version };
    }

    private static bool SameWrites(Dictionary<string, string> first, Dictionary<string, string> second)
        => first.Count == second.Count && first.All(item => second.TryGetValue(item.Key, out var value) && item.Value == value);
}
