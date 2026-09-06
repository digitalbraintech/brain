using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class ActivityStateTests
{
    private static readonly NeuronId Source = new("source", new OwnerId("owner"), "input");
    private static readonly NeuronId Target = new("worker", Source.Owner, "one");

    [Fact]
    public void CompletionWaitsForEveryRegisteredBranchAndDeferredWork()
    {
        var root = Fact("root", "running");
        var state = new ActivityState();
        state.Apply(root);
        state.Apply(root with { OperationId = "branch", CausationId = root.SignalId, Phase = "waiting" });
        state.Apply(root with { Phase = "completed", Timestamp = root.Timestamp.AddSeconds(1) });
        Assert.Equal("waiting", state.View().Status);
        state.Apply(root with { OperationId = "branch", CausationId = root.SignalId, Phase = "completed", Timestamp = root.Timestamp.AddSeconds(2) });
        Assert.Equal("completed", state.View().Status);
    }

    [Fact]
    public void AnObservationNeverBecomesCompletedBecauseTimePassed()
    {
        var state = new ActivityState();
        state.Apply(Fact("journal", "observed"));
        Assert.Equal("observed", state.View().Status);
    }

    [Fact]
    public void DuplicateFactsAndDelayedStartsDoNotReopenSettledWork()
    {
        var root = Fact("root", "running");
        var state = new ActivityState();
        state.Apply(root);
        var completed = root with { Phase = "completed", Timestamp = root.Timestamp.AddSeconds(1) };
        state.Apply(completed);
        state.Apply(completed);
        state.Apply(root);
        Assert.Equal("completed", state.View().Status);
        Assert.Equal(2, state.View().Events.Length);
        Assert.Equal(2, state.View().Version);
    }

    [Fact]
    public void AFailedBranchDoesNotHideOtherRunningBranches()
    {
        var root = Fact("root", "running");
        var state = new ActivityState();
        state.Apply(root);
        state.Apply(root with { OperationId = "child", CausationId = root.SignalId, Phase = "failed" });
        Assert.Equal("running", state.View().Status);
        state.Apply(root with { Phase = "completed", Timestamp = root.Timestamp.AddSeconds(1) });
        Assert.Equal("failed", state.View().Status);
    }

    [Fact]
    public void CausalMetadataAndTheExecutingRevisionAreRetained()
    {
        var root = Fact("root", "running");
        var child = root with { OperationId = "child", SignalId = SignalId.New(), CausationId = root.SignalId, BehaviorRevision = "revision-7" };
        var state = new ActivityState();
        state.Apply(root);
        state.Apply(child);
        var view = state.View();
        Assert.Equal(root.SignalId.ToString(), view.RootSignalId);
        Assert.Equal(root.SignalId.ToString(), view.Events[1].CausationId);
        Assert.Equal("revision-7", view.Events[1].BehaviorRevision);
        Assert.Contains("worker:one", view.ParticipantNeuronIds);
    }

    private static ActivityExecutionChanged Fact(string operation, string phase) => new(
        CorrelationId.New(), null, operation, SignalId.New(), null, Source, Target,
        "UserMessaged", phase, DateTimeOffset.UtcNow, "Research Orleans", "command-1");
}
