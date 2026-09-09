using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

[Alias("db.activities")]
public interface IActivities : INeuron, IHandle<ActivityExecutionChanged>, IHandle<ReadActivities>
{
    const string GrainTypeName = "activities";
    const string DefaultInstanceName = "activities";
}

[Alias("db.activity-source")]
public interface IActivitySource : INeuron, IHandle<ActivityExecutionChanged>
{
    const string GrainTypeName = "activitysource";
    const string DefaultInstanceName = "execution";
}
