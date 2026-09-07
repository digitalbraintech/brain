using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Scripting;

public sealed class EventPort<T>
{
    internal EventPort(string scopeId, string sourceKind, string sourceId, string behaviorKey, string key, string contract)
        => (ScopeId, SourceKind, SourceId, BehaviorKey, Key, Contract) =
            (scopeId, sourceKind, sourceId, behaviorKey, key, contract);
    internal string ScopeId { get; }
    internal string SourceKind { get; }
    internal string SourceId { get; }
    internal string BehaviorKey { get; }
    internal string Key { get; }
    internal string Contract { get; }
}

public sealed class InputPort<T>
{
    internal InputPort(string scopeId, string applicationKey, string operation, string contract)
        => (ScopeId, ApplicationKey, Operation, Contract, NeuronId) = (scopeId, applicationKey, operation, contract, null);
    internal InputPort(string scopeId, NeuronId neuronId, string contract)
        => (ScopeId, ApplicationKey, Operation, Contract, NeuronId) = (scopeId, "", "", contract, neuronId);
    internal string ScopeId { get; }
    internal string ApplicationKey { get; }
    internal string Operation { get; }
    internal string Contract { get; }
    internal NeuronId? NeuronId { get; }
}

public static class ActivityApplicationPorts
{
    public static ActivitySourceEvents Events(this NeuronReference<IActivitySource> source) => new(source);
    public static ActivitiesEvents Events(this NeuronReference<IActivities> source) => new(source);
    public static ActivitiesInputs Inputs(this NeuronReference<IActivities> target) => new(target);
}

public sealed class ActivitiesEvents
{
    internal ActivitiesEvents(NeuronReference<IActivities> source)
        => ActivityChanged = new(source.ApplicationScopeId, "neuron", source.Id.ToString(), "",
            "activity-changed", "activity.changed/v1");
    public EventPort<ActivityChanged> ActivityChanged { get; }
}

public sealed class ActivitySourceEvents
{
    internal ActivitySourceEvents(NeuronReference<IActivitySource> source)
        => ExecutionChanged = new(source.ApplicationScopeId, "neuron", source.Id.ToString(), "",
            "execution-changed", "activity.execution-changed/v1");
    public EventPort<ActivityExecutionChanged> ExecutionChanged { get; }
}

public sealed class ActivitiesInputs
{
    internal ActivitiesInputs(NeuronReference<IActivities> target)
        => ExecutionChanged = new(target.ApplicationScopeId, target.Id, "activity.execution-changed/v1");
    public InputPort<ActivityExecutionChanged> ExecutionChanged { get; }
}

public sealed class BrainRoot
{
    internal BrainRoot(OwnerId owner, PrincipalId principal)
    {
        Id = IBrainNeuron.ForOwner(owner);
        Events = new($"{owner}/{principal}", Id);
    }
    public NeuronId Id { get; }
    public BrainRootEvents Events { get; }
}

public sealed class BrainRootEvents
{
    internal BrainRootEvents(string scopeId, NeuronId source)
        => Activated = new(scopeId, "neuron", source.ToString(), "", "activated", "brain.activated/v1");
    public EventPort<DigitalBrainActivated> Activated { get; }
}
